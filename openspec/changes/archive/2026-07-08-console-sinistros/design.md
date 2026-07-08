## Context

O Motor Antifraude roda ponta a ponta, mas não tem cliente: a `Antifraude.Api` (Minimal API,
.NET 10) expõe `POST /sinistros`, `GET /health` e Swagger, e no bootstrap já toca o
`AntifraudeDbContext` (migrations + seed). O Worker consome a fila e grava `casos` + `auditoria`
de forma assíncrona. Esta mudança é **aditiva e na borda**: adiciona um endpoint de leitura e um
front estático, sem tocar em `Core`, no fluxo de decisão, no Worker ou no `Dockerfile.api`.

Restrições que moldam o design: ports & adapters leve (`Core` sem EF/SQS; composição na borda);
guardrails da vision (nunca veredito, human-in-the-loop, auditoria imutável, não-acusação); a
página deve ser autocontida (vanilla, sem build, sem CDN). As decisões de produto/design vêm do
PRD `docs/features/feature-console-sinistros-demo/prd.md` (§9) e do registro de grilling (§24).

## Goals / Non-Goals

**Goals:**
- Servir um Console web estático pela própria API (mesma origem, sem CORS) que envia sinistros
  reais e mostra o ciclo assíncrono do caso.
- Expor `GET /casos/{caseId}` somente-leitura que devolve caso + trilha de ingestão + trilha de
  decisão, com estados honestos (processado / recebido-mas-não-processado / inexistente).
- Encarnar a não-acusação na apresentação e gate de acesso graduado por ambiente (default seguro).
- Zero fricção: sobe no `docker compose up` existente, sem novo container, sem toolchain de front.

**Non-Goals:**
- Painel do Analista com ações/decisão (Feature 2.7); listagem de casos no servidor.
- Porta de leitura no `Core`; alteração de `POST /sinistros`, Worker ou domínio.
- IdP real, multiusuário com perfis ricos, upload de imagem.

## Decisions

### D1 — Endpoint `GET /casos/{caseId}` na borda, lendo o DbContext direto
Um único handler Minimal API injeta `AntifraudeDbContext` e faz três SELECT (`casos`,
`auditoria_ingestao`, `auditoria`) por `caseId`, projetando para um DTO anônimo. Enums
(`Estado`, `Faixa`, `Rota`, `Idempotencia`, `Destino`) serializados como **string** via
`.ToString()` na projeção (evita configurar `JsonStringEnumConverter` global e mudar outros
endpoints). Semântica de resposta:
- caso + trilhas presentes → `200 { encontrado: true, caso, ingestao[], auditoria[] }`;
- só trilha de ingestão (Worker ainda não gravou o caso) → `200 { encontrado: false, ingestao[] }`;
- nada → `404 { encontrado: false }`.

*Alternativas:* porta `IConsultaCaso` no `Core` (rejeitada — a 2.7 reimplementa leitura com
busca/filtros/paginação, então a porta seria descartável; a leitura na borda é scaffolding
honesto e a `Api` já toca o DbContext no bootstrap, sem ferir a pureza do `Core`); reusar
`ICaseRepository.ObterPorIdAsync` só para o caso (rejeitada — misturaria dois estilos de acesso
no mesmo handler, já que as trilhas não têm porta de leitura).

### D2 — Front estático vanilla servido de `wwwroot`
`app.UseDefaultFiles()` + `app.UseStaticFiles()` na `Api`, com `wwwroot/` contendo
`index.html`, `styles.css`, `app.js` e a fonte mono auto-hospedada (`wwwroot/fonts/`). O
`Microsoft.NET.Sdk.Web` inclui `wwwroot` no `dotnet publish` por padrão → **sem mudança no
`Dockerfile.api`**. Sem framework, sem `node_modules`, sem etapa de build. Ordem no pipeline:
`UseDefaultFiles`/`UseStaticFiles` antes do Swagger e dos endpoints; a raiz `/` serve o Console,
Swagger permanece em `/swagger`.

*Alternativa:* framework + build (rejeitada no grilling — toolchain e manutenção de deps para um
escopo de "enviar + observar").

### D3 — Gate de acesso graduado por ambiente (default seguro)
Configuração (env var, ex.: `Console__Modo` = `local` | `compartilhado` | `desabilitado`, e
`Console__Credenciais` para o modo compartilhado). Um filtro único aplicado **ao Console e ao
`GET /casos`**:
- `local` → libera sem autenticação (default quando `ASPNETCORE_ENVIRONMENT=Development`);
- `compartilhado` → exige **Basic auth** (prompt nativo do navegador; a mesma credencial vale
  para o `fetch` same-origin) contra a credencial configurada;
- não configurado fora de `Development` → **desabilitado** (Console responde 404; `GET /casos`
  responde 403) — nada abre por acidente.

Basic auth é a escolha pragmática para uma página servida ao navegador (o browser trata o
credential prompt e reenvia nos `fetch`). Marcado como substituível pelo IdP da plataforma
quando a Feature 2.7 definir o padrão.

*Alternativas:* IdP/OIDC agora (rejeitada — sem padrão definido, arriscaria contradizer a 2.7);
só controle por rede (rejeitada — confia inteiramente na infra, arriscado se a porta vazar).

### D4 — Badge de ambiente + saúde via `/health` estendido
`GET /health` passa a devolver `{ status, ambiente }` (nome do ambiente vindo de config, fallback
`IHostEnvironment.EnvironmentName`). O Console chama `/health` no load para o indicador de
conectividade e o badge de ambiente. Nenhum dado sensível é exposto; em modo compartilhado o
`/health` fica atrás do mesmo gate.

### D5 — Mecânica do stepper + polling (client-side)
Após o `POST`, o `app.js` inicia polling de `GET /casos/{caseId}` a cada ~1s, timeout ~20s. O
stepper tem quatro estágios (recebeu → ingestão → worker → caso). O mapeamento honesto vem da
resposta do `GET`: quando a trilha de ingestão traz `destino = Descartado` (duplicado) ou
`FilaErroTecnico` (sem `idSinistro`), o polling **para de imediato** e o stepper marca o ramo
terminal; quando `destino = FilaProcessamento`, segue até `encontrado = true` (caso pronto). O
"pulso" é uma animação CSS ao longo do traço; sob `prefers-reduced-motion` só o estado dos
pontos muda. O comprovante inicial (202/400/503 + `caseId`) é derivado da resposta do `POST`.

### D6 — Cenários e idempotência reproduzível
Cenários são objetos JS que preenchem o formulário. "Novo" gera `idSinistro` fresco a cada carga
(`crypto.randomUUID()` encurtado / prefixo + aleatório). "Duplicado" lê uma variável de sessão
`ultimoIdEnviado` (setada a cada envio bem-sucedido); sem envio prévio, usa um id de exemplo e
sinaliza "envie o completo primeiro". "Corpo ilegível" é um caminho especial: envia um corpo cru
malformado (string) com `Content-Type: application/json`, ignorando o formulário, para acionar o
400 real; exibe aviso de que é intencional.

### D7 — Tokens de design e temas (PRD §9)
Paleta e tipografia como CSS custom properties em `:root`, com override por
`@media (prefers-color-scheme: dark)` e por `:root[data-theme=...]` (toggle). Acento
índigo-violeta; cores semânticas (ok/atenção/crítico) **exclusivas da saúde do pipeline**, nunca
da faixa de risco. Mono auto-hospedada via `@font-face` local como face de display + dados; corpo
em stack de sistema. Assinatura: o "traço de sinal" (stepper como linha de instrumento).

### D8 — Não-acusação como regra de UI
O cartão do caso destaca o estado ("aceito — encaminhado para análise humana" / "Pendente de
revisão manual"); faixa/rota/score aparecem como "sinais de priorização" neutros. Nenhum texto
"fraude/aprovado/negado"; nenhuma cor semântica aplicada à faixa.

### D9 — Testes
Integração (Testcontainers, como o resto da suíte) para `GET /casos/{caseId}`: caso processado,
recebido-mas-não-processado, inexistente (404), e garantia read-only (nenhuma escrita). Teste do
gate de acesso (local libera; compartilhado sem credencial → 401/403; desabilitado → 404). O
front vanilla não tem teste unitário automatizado; validação é manual pelos 5 cenários + os
critérios Gherkin dos specs.

## Risks / Trade-offs

- **Leitura via `DbContext` na borda diverge da convenção de portas** → isolada num único handler
  e documentada como scaffolding; candidata a ser absorvida pela 2.7.
- **Basic auth é autz mínima** → aceitável para ferramenta interna não-produtiva; default seguro
  impede exposição acidental; caminho de IdP no roadmap.
- **Polling gera carga de requisições** → cadência baixa (~1s), timeout curto (~20s) e corte cedo
  nos ramos terminais limitam o custo; SSE/WebSocket fica no roadmap.
- **`/health` passa a revelar o nome do ambiente** → dado não sensível e, em modo compartilhado,
  atrás do gate.
- **Enum serializado por `.ToString()` na projeção** → acoplamento leve ao nome do enum; preferido
  a um conversor global que afetaria outros endpoints.
- **Design "instrumento" pode descambar em dashboard genérico** → boldness concentrada na
  assinatura (traço de sinal), resto quieto (PRD §9.5).

## Migration Plan

Mudança **aditiva, sem migração de banco** (endpoint só lê; nenhum novo schema). Deploy junto com
a `Api` no `docker compose up` existente. `.env.example` ganha as chaves do gate de acesso
(`Console__Modo`, `Console__Credenciais`) com default local. **Rollback:** remover o endpoint
`GET /casos`, o `UseStaticFiles`/`wwwroot` e as chaves de config — sem efeito colateral em dados,
já que nada é escrito.

## Open Questions

- Face mono a auto-hospedar: JetBrains Mono vs. IBM Plex Mono (decisão de execução no apply).
- Nomes finais das chaves de configuração do gate (`Console__Modo`/`Console__Credenciais` são
  propostas).
- Manter `ambiente` dentro de `GET /health` vs. endpoint dedicado `GET /console/info` (proposta:
  estender `/health` para não criar superfície nova).
