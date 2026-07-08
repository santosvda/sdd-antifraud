# PRD — Motor Antifraude de Sinistros
## Feature (ferramenta): Console de Sinistros — cliente de operação e QA
**ACME Seguros — Seguro de Celular — Trilha B (sinistro por foto)**

---

> **Natureza desta feature:** é uma **ferramenta transversal de operação e QA** — um cliente
> web que **consome a API real** do motor para enviar sinistros e acompanhar o ciclo do caso
> em tela. **Estreia no workshop** (onde substitui Swagger/`curl` + inspeção de logs por uma
> tela única), mas é **projetada para uso contínuo** por QA e pela equipe antifraude como
> cliente de teste manual e janela de observação do pipeline. Numeração fora da série 2.x
> porque **não é uma capacidade antifraude** (não coleta sinais, não calcula score, não decide
> mérito) — é ferramenta de apoio ao produto. Diferente do **Painel do Analista (Feature
> 2.7)**, que é a superfície operacional *de decisão humana* sobre casos; aqui não há ação
> sobre o mérito — só **enviar e observar** (ver §13.1, Fronteira com a Feature 2.7).
>
> As decisões de design e escopo desta versão vieram de uma sessão de grilling — o registro
> completo está no §24.

## 1. Visão Geral

Esta feature entrega um **cliente web ("Console de Sinistros") servido pela própria API** que
dispara um `POST /sinistros` **real** contra o ambiente rodando e exibe, em tela, o
**comprovante de ingestão** e a **evolução assíncrona do caso** até ele virar
`PendenteRevisaoManual` com auditoria carimbada. É a peça que faltava para **exercitar e
inspecionar** o motor: hoje ele funciona (`POST /sinistros → SQS → Worker → MySQL`), mas
**nenhum cliente o consome** — a única forma de acioná-lo é via Swagger/`curl` e leitura de
logs/banco.

O Console nasce para o workshop, mas é pensado como **ferramenta reutilizável de QA/operação**:
um cliente de teste manual estável, com cenários reproduzíveis, que qualquer pessoa da equipe
usa para validar a ingestão ponta a ponta e observar o comportamento do pipeline em ambientes
não-produtivos (dev, QA, homologação). Ele torna tangível o guardrail central: o resultado em
tela é **"aceito para análise humana"**, nunca um veredito de fraude.

Esta feature **não** altera o domínio (`Core`), **não** introduz nenhuma decisão de mérito
(nem no front, nem no endpoint de leitura) e **não** persiste nem edita casos pela tela.

## 2. Problema

O motor está no ar, mas é "cego" para quem o opera: exercitá-lo exige abrir Swagger, montar
JSON à mão e depois vasculhar `docker compose logs` ou o MySQL para ver o caso. Isso é frágil
numa apresentação, é lento no dia a dia de QA, não conta a história dos guardrails e não deixa
claro o caráter assíncrono do pipeline. Falta uma superfície única, estável e reutilizável
onde um envio real produza um retorno legível e o ciclo completo apareça na tela — utilizável
tanto ao vivo num workshop quanto como cliente de teste recorrente pela equipe.

## 3. Objetivos

- Disparar um `POST /sinistros` **real** (mesma origem, sem CORS) a partir de uma página web
  servida pela própria API, sem toolchain de frontend nem etapa de build.
- Exibir o **comprovante de ingestão** de forma legível: `caseId`, HTTP status, idempotência,
  destino de roteamento, marca de payload parcial.
- Fechar o ciclo: consultar um endpoint **read-only** e mostrar o caso evoluir para
  `PendenteRevisaoManual` + a trilha de auditoria, evidenciando o processamento assíncrono por
  meio de um **stepper de pipeline**.
- Oferecer **cenários pré-montados** que cobrem os caminhos do handler (novo, duplicado,
  parcial, sem `idSinistro`, corpo ilegível), para uma operação/demo redonda e reproduzível.
- Ser uma **ferramenta reutilizável de QA/operação**: estável, sem dependências externas, apta
  a virar cliente de teste manual recorrente em ambientes não-produtivos.
- Reforçar a narrativa dos guardrails: o retorno é "aceito para análise humana", não
  "fraude/não fraude"; o endpoint de leitura não decide nada.
- Ter um **caminho de segurança claro**: sem autenticação em uso local, com autz obrigatória
  (default seguro) antes de qualquer exposição compartilhada (ver §13).

**Não-objetivos desta feature:** alterar o fluxo de decisão do motor, montar a UI de decisão
do analista (isso é a Feature 2.7), calcular ou exibir qualquer veredito de fraude,
persistir/editar estado do caso pela tela, virar um cliente de produção voltado ao cliente
final.

## 4. Personas

| Persona | Papel nesta feature |
|---|---|
| **QA / Equipe antifraude** | Usuário primário recorrente — usa o Console como cliente de teste manual e janela de observação do pipeline em dev/QA/homologação. |
| **Desenvolvedor(a) do motor** | Smoke test manual rápido durante o desenvolvimento, sem Swagger/`curl`. |
| **Apresentador/instrutor** | Opera o Console ao vivo no workshop; primeiro cenário de uso. |
| **Plateia do workshop** | Observa o envio real e o resultado em tela; público da narrativa. |
| **API (Feature 2.1)** | Superfície consumida: `POST /sinistros` (existente) + `GET` de leitura (novo). |

## 5. Jornada do Usuário (QA / Apresentador)

1. Sobe o ambiente com `docker compose up` e abre `http://localhost:8080/`.
2. Ao carregar, o Console mostra o **badge de ambiente** e a **saúde da API** (health check).
3. Escolhe um cenário pré-montado (que preenche o formulário) ou edita os campos e clica em
   **Enviar sinistro**.
4. Vê o comprovante de ingestão: status HTTP, `caseId`, idempotência e destino.
5. O **stepper de pipeline** acende os estágios conforme o polling revela o estado, até o caso
   virar `PendenteRevisaoManual`, com a auditoria carimbada.
6. Repete com "reenvio" (idempotência), "sem idSinistro" (erro técnico) e "corpo ilegível"
   (400); cada envio entra no **histórico da sessão** para comparar lado a lado.
7. (Uso QA recorrente) valida uma mudança na ingestão em homologação, checando o ciclo completo
   e os **detalhes técnicos** (JSON cru) sem tocar em Swagger/banco.

## 6. Fluxo Completo (envio real + polling do ciclo)

```
[Navegador — Console de Sinistros]  (servido de wwwroot pela própria API)
        │  POST /sinistros (fetch, mesma origem, sem CORS)
        ▼
┌─────────────────────────────┐
│ API — POST /sinistros (2.1)    │  → 202 { caseId } | 400 (ilegível) | 503 (broker fora)
└───────────────┬────────────────┘
                │ 202 + caseId
                ▼
┌─────────────────────────────┐
│ Console exibe COMPROVANTE      │  caseId · idempotência · destino · payloadParcial
└───────────────┬────────────────┘
                │ polling GET /casos/{caseId}  (~1s, timeout ~20s)
                ▼
┌─────────────────────────────┐        (Worker consome SQS → MotorDeDecisao →
│ API — GET /casos/{caseId}      │◀──────  grava caso + auditoria, assíncrono)
│ (read-only, novo)              │
└───────────────┬────────────────┘
    destino?    │
   ┌────────────┼─────────────────────────────┐
   ▼ Fila proc. ▼ Descartado (dup)   ▼ Fila erro técnico (sem id)
 segue polling  PARA polling —        PARA polling —
 até o caso     estágio terminal      estágio terminal
   │            "descartado"          "erro técnico" (não gera caso)
   ▼ caso pronto
 Console exibe CASO + AUDITORIA
 estado=PendenteRevisaoManual, faixa/rota/score (priorização neutra),
 versões, trilha de ingestão, trilha de decisão
```

**Ponto crítico do guardrail:** o Console nunca exibe "fraude/não fraude". O melhor resultado
possível em tela é **"aceito e encaminhado para análise humana"**. O `GET` é leitura pura — não
altera estado nem expressa decisão.

## 7. Regras de Negócio

1. O front é **estático, vanilla** (HTML/CSS/JS puro, sem build e sem dependências externas)
   servido de `wwwroot` pela própria API — mesma origem, portanto **sem CORS**.
2. O envio usa `fetch` real para `POST /sinistros`; o Console não replica nem "adivinha" a
   decisão da API — apenas exibe a resposta recebida.
3. A entrada é um **formulário estruturado** (campos nomeados do payload real), pré-preenchido
   por **cenários** disponíveis como chips de carga rápida.
4. Os cenários pré-montados são cinco: (a) completo e novo, (b) reenvio duplicado, (c) payload
   parcial (só `idSinistro`), (d) sem `idSinistro`, (e) corpo ilegível.
5. **Idempotência reproduzível:** o cenário "novo" gera um `idSinistro` fresco a cada carga
   (sempre PrimeiraVez); o cenário "duplicado" reusa o `idSinistro` do último envio
   bem-sucedido da sessão (se ainda não houve, usa um id de exemplo e sinaliza "envie o
   completo primeiro").
6. **Cenário "corpo ilegível":** é um chip dedicado que envia um corpo cru propositalmente
   malformado (ignorando o formulário), com aviso na tela de que é intencional, para demonstrar
   que o 400 é a **única** rejeição de formato — nunca uma decisão de fraude.
7. O `GET /casos/{caseId}` é **somente leitura**: retorna o caso (estado, faixa, rota, score,
   versões, `payloadParcial`, `criadoEm`), a trilha de ingestão e a trilha de auditoria; nunca
   escreve nem decide.
8. **Polling:** cadência ~1s, timeout ~20s. Nos destinos terminais de ingestão (duplicado →
   Descartado; sem `idSinistro` → Fila de erro técnico) o Console **para o polling
   imediatamente** e marca o estágio terminal — não espera um caso que não virá. No timeout do
   caminho feliz, exibe "Worker ainda processando — verifique os logs", sem travar.
9. **Histórico da sessão:** cada envio entra numa lista mantida em memória no navegador; nada é
   persistido. Não há endpoint de listagem no servidor.
10. Payload de fotos é sempre **referência** (URL/ID), coerente com a minimização já adotada na
    ingestão — o Console não faz upload de imagem.
11. O Console **não** oferece nenhuma ação sobre o mérito do sinistro nem sobre o estado do
    caso — só enviar e observar.
12. Nenhuma credencial, segredo ou dado sensível é embutido na página estática.
13. **Autz por ambiente (default seguro):** em uso local/dev, roda sem autenticação; ao ser
    exposto para acesso compartilhado, o acesso ao Console e ao `GET` exige token simples
    (config), e o Console fica **desabilitado por padrão** se o modo não-local não estiver
    explicitamente configurado (ver §13).

## 8. Arquitetura de Alto Nível

```
┌──────────────────────────────────────────────┐
│ Antifraude.Api  (http://localhost:8080)          │
│                                                  │
│  wwwroot/index.html + app.js + styles.css        │
│  (Console — vanilla) ── UseDefaultFiles +        │
│        │                  UseStaticFiles          │
│        │  fetch (mesma origem)                    │
│        ├───────────▶ POST /sinistros  (2.1, já existe)
│        │                     │ enfileira → SQS    │
│        ├───────────▶ GET /casos/{caseId} (NOVO, read-only)
│        │                     │ lê via              │
│        │                     ▼                     │
│        │            AntifraudeDbContext (borda)    │
│        │            casos · auditoria ·            │
│        │            auditoria_ingestao (SELECT)    │
│        └───────────▶ GET /health + info de ambiente (badge)
└──────────────────────────────────────────────┘
        (Worker consome SQS e grava casos/auditoria — inalterado)
```

- `Core` **intocado**. A leitura fica na borda (`Api`), **consultando `AntifraudeDbContext`
  diretamente, isolada num único ponto** (SELECT é livre; a imutabilidade cobre apenas
  UPDATE/DELETE via trigger). Escolha deliberada: como a Feature 2.7 vai reimplementar leitura
  com necessidades mais ricas (busca, filtros, paginação), evita-se criar uma porta no Core
  agora que a 2.7 logo substituiria — é scaffolding honesto e fácil de remover.
- O modo de acesso (local livre × compartilhado com token; default seguro) é **governado por
  configuração de ambiente**, não hard-coded (RF10, §13).
- `wwwroot` é incluído no `dotnet publish` por padrão — **sem alteração no `Dockerfile.api`**.

## 9. Design & Frontend (formulação frontend-design)

> **Subject:** um **instrumento de bancada** que dispara sinistros reais e observa o caso
> atravessar um pipeline de detecção de *sinais*, terminando em "aceito para análise humana".
> Audiência: QA/antifraude/dev + plateia. Trabalho único da página: **enviar e observar** o
> ciclo com honestidade, sem nunca exibir veredito.

### 9.1 Direção e princípios

**Painel de instrumento de backoffice.** Estética calma e densa em informação, coerente com
"instrumenta o analista" e "100% backoffice" da vision. Princípio inegociável de cor: as
**cores semânticas de status (ok / atenção / crítico) são reservadas à saúde do pipeline**
(recebido, processando, timeout, erro) — **nunca** à faixa de risco, para que "alto" jamais
vire um carimbo vermelho de "culpado". O acento é uma cor **distinta das três semânticas**.

### 9.2 Tokens de cor

Neutros com **viés azul** (escolhidos, não default). Acento **índigo-violeta** — deliberadamente
fora do "azul de dashboard" e distinto de verde/âmbar/vermelho semânticos. Definidos como
custom properties, com tema pela mídia + override por `data-theme`.

| Token | Claro | Escuro | Uso |
|---|---|---|---|
| `--bench` (fundo) | `#ECEFF2` | `#0C1316` | fundo da "bancada" |
| `--panel` (superfície) | `#FAFBFC` | `#121C21` | painéis/cartões |
| `--ink` (texto) | `#141D26` | `#E8EEF1` | texto principal |
| `--muted` | `#586673` | `#869AA6` | rótulos/secundário |
| `--line` (hairline/grid) | `#D2D9DF` | `#21303A` | traços, grade, divisórias |
| `--accent` (sinal) | `#3E33C7` | `#7C6BFF` | acento único (ações, pulso) |
| `--ok` | `#158A5E` | `#33C489` | status pipeline: ok |
| `--warn` | `#B67A1E` | `#E0A64A` | status pipeline: atenção/timeout |
| `--crit` | `#BC4038` | `#E56258` | status pipeline: erro |

### 9.3 Tipografia

- **Display / eyebrows / dados:** **monoespaçada** (a face de display, não só utilitária — é o
  que crava o ar de "leitura de instrumento"). Recomendado auto-hospedar uma face com caráter
  (ex.: JetBrains Mono ou IBM Plex Mono) via `@font-face` local — **sem CDN** (a página é
  autocontida); fallback `ui-monospace, "Cascadia Code", Menlo, Consolas, monospace`.
- **Corpo:** sans humanista do sistema (`system-ui, -apple-system, "Segoe UI", Roboto,
  sans-serif`) para leitura confortável dos textos explicativos.
- Escala de tipos definida e respeitada; eyebrows em maiúsculas com tracking; `tabular-nums`
  onde houver dígitos alinhados (score, timestamps).

### 9.4 Layout

Bancada de **dois painéis**: à esquerda o **Painel de Controle** (cenários + formulário +
Enviar); à direita o **Readout** (comprovante → traço de sinal → caso + auditoria → detalhes
técnicos). Barra de instrumento no topo (título, badge de ambiente, lâmpada de saúde, toggle de
tema). Em telas estreitas, empilha: controle em cima, readout embaixo. Grade sutil de
esquemático **apenas atrás do readout**, para não competir com a assinatura.

```
┌───────────────────────────────────────────────────────────────┐
│ CONSOLE DE SINISTROS        [● dev]   saúde: ● online   [◐ tema]│
├──────────────────────┬────────────────────────────────────────┤
│ PAINEL DE CONTROLE   │ READOUT                                  │
│ cenários:            │  ╭─ comprovante ───────────────╮         │
│ [novo][dup][parcial] │  │ 202 · caseId 3f9a… · aceito  │         │
│ [sem id][ilegível]   │  ╰──────────────────────────────╯         │
│ idSinistro [______]  │   ●━━━━●━━━━◌╌╌╌╌○   ← traço de sinal    │
│ apólice    [______]  │  recebeu ingest. worker caso             │
│ aparelho   [______]  │        └─▶ descartado / erro técnico     │
│ fotos      [+ ref ]  │  ╭─ caso ──────────────────────╮         │
│ metadados  [______]  │  │ Pendente de revisão manual   │         │
│   [ Enviar sinistro ]│  │ priorização: faixa · rota     │         │
│                      │  ╰──────────────────────────────╯         │
│ histórico da sessão  │  ▸ detalhes técnicos (recolhido)         │
│  · 3f9a  aceito      │                                          │
│  · 7b21  duplicado   │                                          │
└──────────────────────┴────────────────────────────────────────┘
```

### 9.5 Elemento-assinatura — o "traço de sinal"

O stepper de pipeline **é** a assinatura: uma linha de instrumento (estilo traço de
osciloscópio) atravessando o readout, com os estágios como **pontos de prova** (recebeu →
ingestão → worker → caso) e o caso como um **pulso** que viaja pela linha conforme o polling
avança. Ramos terminais **bifurcam para baixo** da linha (duplicado → "descartado"; sem id →
"erro técnico"), tornando visível que ali o caso não segue. É o único elemento ousado — todo o
resto fica quieto e disciplinado.

### 9.6 Movimento

O pulso percorre o traço quando um estágio é alcançado; o ponto de prova "acende" ao ser
atingido. Nada de animação gratuita. `prefers-reduced-motion` respeitado: sem viagem do pulso,
apenas a mudança de estado dos pontos.

### 9.7 Copy / tom (não-acusação)

- Ação: **"Enviar sinistro"** → comprovante **"Recebido — aceito para análise humana"**.
- O estado domina o cartão do caso: **"Pendente de revisão manual"**. Faixa/rota/score aparecem
  como **"sinais de priorização"** secundários, com rótulos neutros — nunca "fraude/aprovado".
- Vazio: "Envie um sinistro para ver o ciclo." · Timeout: "Worker ainda processando — verifique
  os logs do worker." · Sem API: "API não encontrada — rode `docker compose up`."
- Voz ativa, sentence case, sem jargão acusatório em nenhum ponto (honra a vision).

### 9.8 Acessibilidade e piso de qualidade

Responsivo até o mobile (empilhamento), foco de teclado visível, contraste legível nos dois
temas, `prefers-reduced-motion` respeitado, sem scroll horizontal (conteúdo largo — JSON cru —
rola no próprio contêiner).

## 10. Requisitos Funcionais

| ID | Requisito |
|---|---|
| RF01 | A API deve servir uma página estática vanilla ("Console de Sinistros") em `http://localhost:8080/` via `UseDefaultFiles()` + `UseStaticFiles()`, a partir de `wwwroot`, sem etapa de build nem dependências externas. |
| RF02 | O Console deve montar e enviar um `POST /sinistros` real (mesma origem, sem CORS) a partir de um **formulário estruturado** com os campos do payload real (`idSinistro`, `apolice`, `aparelho.imei/numeroSerie`, `fotos[]`, `metadados.abertoEm/canal/idCliente`). |
| RF03 | O Console deve oferecer os cenários pré-montados como chips: (a) completo e novo, (b) reenvio duplicado, (c) payload parcial, (d) sem `idSinistro`, (e) corpo ilegível. |
| RF04 | O cenário "novo" deve gerar um `idSinistro` fresco a cada carga; o "duplicado" deve reusar o `idSinistro` do último envio bem-sucedido da sessão. |
| RF05 | O cenário "corpo ilegível" deve enviar um corpo cru malformado (ignorando o formulário), com aviso de que é intencional. |
| RF06 | O Console deve exibir o comprovante de ingestão: status HTTP, `caseId` (quando 202), e o significado do resultado (aceito / duplicado / erro técnico / rejeição de formato). |
| RF07 | A API deve expor `GET /casos/{caseId}` **somente leitura**, retornando o caso, a trilha de ingestão e a trilha de auditoria correlacionadas, ou "não encontrado" quando o caso ainda não foi processado. |
| RF08 | O Console deve consultar `GET /casos/{caseId}` em polling (~1s) até o caso aparecer ou até o timeout (~20s), exibindo o estado transitório num **stepper de pipeline**; deve parar cedo nos destinos terminais (Descartado / Fila de erro técnico). |
| RF09 | Ao encontrar o caso, o Console deve exibir estado (`PendenteRevisaoManual`) em destaque e faixa/rota/score como priorização neutra, além das trilhas de auditoria; cores semânticas ficam restritas à saúde do pipeline. |
| RF10 | O Console deve manter um **histórico da sessão** (em memória, client-side) dos envios, cada item reabrindo o polling do seu `caseId`. |
| RF11 | O Console deve oferecer uma seção recolhível de **detalhes técnicos** com a requisição/resposta cruas (método/URL, status, corpos JSON de POST e GET). |
| RF12 | Ao carregar, o Console deve chamar `GET /health` e exibir um indicador de conectividade e um **badge de ambiente-alvo**; se a API estiver inacessível, exibir aviso claro. |
| RF13 | Nenhum elemento do Console nem do endpoint de leitura deve expressar decisão de mérito (fraude, cobertura, indenização). |
| RF14 | O endpoint de leitura não deve, em nenhuma circunstância, escrever, atualizar ou remover estado. |
| RF15 | O acesso ao Console e ao `GET` deve ser **governado por configuração de ambiente**: liberado em local/dev; protegido por token simples quando exposto; **desabilitado por padrão** se o modo não-local não for configurado. |

## 11. Requisitos Não Funcionais

- **Zero fricção de setup:** sobe com o `docker compose up` existente; sem novo container, sem
  CORS, sem build de frontend, sem dependências externas (fontes auto-hospedadas).
- **Reutilizável e estável:** apta a uso recorrente por QA/operação; cenários e layout estáveis
  entre versões do motor.
- **Portabilidade:** página autocontida, funciona em qualquer navegador moderno.
- **Responsividade da tela:** o resultado (comprovante + ciclo) aparece em poucos segundos.
- **Não-invasividade:** nenhuma mudança de comportamento no domínio ou no fluxo de decisão; a
  feature é aditiva.
- **Design & acessibilidade:** conforme §9 — dois temas com contraste cuidado, foco visível,
  `prefers-reduced-motion`, sem scroll horizontal.
- **Segurança configurável:** sem segredos no cliente; leitura sem efeitos colaterais; autz
  ativável por ambiente com default seguro.

## 12. Integrações

- **API — `POST /sinistros`** (Feature 2.1, existente): superfície de envio.
- **API — `GET /casos/{caseId}`** (novo, desta feature): superfície de leitura do ciclo.
- **API — `GET /health` + info de ambiente:** conectividade e badge de ambiente no load.
- **MySQL** (via `AntifraudeDbContext`): fonte de leitura de `casos`, `auditoria`,
  `auditoria_ingestao`.
- **Ambiente Docker existente** (api + worker + mysql + localstack): sem alterações de
  topologia.
- **Config de token/ambiente** (quando exposto além do local): fonte da autz simples (ver §13).

## 13. Segurança e LGPD

Como ferramenta **reutilizável** (e não descartável), a segurança é tratada como cidadã de
primeira classe, com um modelo **graduado por ambiente e default seguro**:

- **Uso local/dev:** roda sem autenticação em `localhost`, para não criar fricção no workshop e
  no desenvolvimento.
- **Uso compartilhado (QA/homologação):** o acesso ao Console e ao `GET /casos/{caseId}` exige
  um **token simples** (Basic/bearer vindo de configuração). O modo é selecionado por
  **configuração de ambiente** (RF15); se o modo não-local não estiver configurado, o Console
  fica **desabilitado** — nada abre por acidente. A integração com o **IdP real da plataforma
  fica adiada** para quando a Feature 2.7 definir o padrão de identidade, evitando escolher um
  modelo que a 2.7 depois contradiga.
- **Produção voltada ao cliente final:** **fora de escopo** — esta ferramenta é interna.
- **Minimização (LGPD):** a página não coleta nem armazena dados pessoais além do que o
  operador digita para o teste; nada é persistido (o histórico é volátil, em memória). Fotos são
  **referenciadas** (URL/ID), nunca enviadas/duplicadas pelo Console.
- **Exposição de dados:** o `GET` devolve apenas o que já é auditado internamente; ao ser
  exposto, deve respeitar mascaramento de identificadores sensíveis coerente com a política do
  motor.
- **Base legal:** legítimo interesse / prevenção à fraude — a mesma do restante do motor.
- **Segredos:** nenhum segredo/credencial embutido no cliente estático.

### 13.1 Fronteira com a Feature 2.7 (Painel do Analista)

| Aspecto | Console de Sinistros (esta feature) | Painel do Analista (Feature 2.7) |
|---|---|---|
| Propósito | Enviar sinistros e **observar** o ciclo (cliente de teste/QA) | **Decidir** sobre casos (revisão humana) |
| Ação sobre o mérito | Nenhuma — só leitura/observação | Sim — é a superfície de decisão humana |
| Público | QA, dev, apresentador | Analista antifraude |
| Escrita de estado | Nunca | Sim (encaminhamentos/decisões auditadas) |

O Console **não** substitui o Painel do Analista nem deve ganhar ações de decisão. Se a leitura
de casos precisar de recursos ricos (busca, filtros, filas), essa capacidade pertence à 2.7 — o
`GET` desta feature é um mínimo de leitura, candidato a ser **absorvido/reusado** pela 2.7
quando ela existir (ver §22).

## 14. Auditoria

Esta feature **não cria** novos registros de auditoria — ela **lê e exibe** os que o motor já
produz:
- Trilha de ingestão (`auditoria_ingestao`): idempotência, destino, presença de campos,
  `payloadParcial`, timestamp.
- Trilha de decisão (`auditoria`): sinais, score, faixa, rota, versões, causa (fail-open),
  ator, timestamp.
O caráter imutável dessas trilhas é preservado — o endpoint de leitura só executa SELECT. Quando
o acesso compartilhado exigir autz (§13), o **acesso de leitura** pode ele próprio ser
registrado, conforme a política de observabilidade (Feature 2.9).

## 15. Casos de Uso

1. **Envio completo e novo:** cenário "completo" → 202 + `caseId`; o stepper acende até o caso
   `PendenteRevisaoManual` com auditoria carimbada (score `null` = fail-open, sem sinais).
2. **Reenvio duplicado:** reusa o `idSinistro` do envio anterior → 202; o stepper para no
   estágio terminal "Descartado" (idempotência), sem novo caso.
3. **Payload parcial:** só `idSinistro` → 202 + caso marcado `payloadParcial` (fail-open, não
   rejeita por incompletude).
4. **Sem `idSinistro`:** evento não-processável → 202 + estágio terminal "Fila de erro técnico";
   o Console explica que o sinistro já existe no sistema principal, sem impacto ao cliente.
5. **Corpo ilegível:** chip dedicado → 400; o Console mostra que essa é a **única** rejeição de
   formato, não uma decisão de fraude.
6. **Validação de QA em homologação:** após uma mudança na ingestão, QA roda os 5 cenários e
   confirma o ciclo ponta a ponta pelos detalhes técnicos — sem Swagger/banco.

## 16. Casos de Exceção

| Exceção | Tratamento |
|---|---|
| Caso ainda não processado quando o polling começa | Stepper exibe "Worker processando…" e continua consultando até aparecer ou até o timeout. |
| Timeout de polling atingido | Exibe "Worker ainda processando — verifique os logs", sem travar; permite novo envio. |
| Destino terminal (duplicado / sem idSinistro) | Para o polling de imediato e marca o estágio terminal correspondente; não espera caso. |
| Broker fora ao enviar (503) | Exibe o 503 como fronteira do fail-open (única situação em que a API sinaliza o produtor), sem interpretar como fraude. |
| `GET /casos/{id}` para `caseId` inexistente | Trata como caso ainda não processado (segue polling até o timeout). |
| API/DB indisponível para o `GET` ou `/health` | Indicador de saúde em vermelho + aviso claro; não impede novos envios. |
| Acesso sem token em ambiente que exige autz | O Console/`GET` recusa o acesso (401) conforme a configuração de ambiente (RF15). |

## 17. Critérios de Aceite (Gherkin)

```gherkin
Funcionalidade: Console de Sinistros consome a API real e mostra o ciclo em tela

  Cenário: Console é servido pela própria API e sinaliza ambiente/saúde
    Dado que o ambiente subiu com "docker compose up"
    Quando eu abro "http://localhost:8080/"
    Então o Console de Sinistros deve ser exibido, servido pela própria API, sem CORS
    E deve mostrar o badge de ambiente e o indicador de saúde da API

  Cenário: Envio real produz comprovante de ingestão
    Dado que carrego o cenário "completo e novo" (idSinistro fresco)
    Quando clico em "Enviar sinistro"
    Então a API deve responder 202 com um caseId
    E o Console deve exibir o comprovante "aceito para análise humana" com o caseId

  Cenário: Ciclo assíncrono aparece no stepper via polling
    Dado um envio válido que retornou 202 e um caseId
    Quando o Worker processa o sinistro
    Então o stepper deve acender os estágios até "Caso pronto" (PendenteRevisaoManual)
    E deve exibir a trilha de auditoria correlacionada

  Cenário: Duplicado para no estágio terminal
    Dado que já enviei um sinistro com um idSinistro específico
    Quando reenvio o mesmo idSinistro pelo cenário "duplicado"
    Então o stepper deve parar no estágio terminal "Descartado"
    E nenhum novo caso adicional deve ser criado para aquele idSinistro

  Cenário: Corpo ilegível é a única rejeição de formato
    Dado o chip de cenário "corpo ilegível"
    Quando envio a requisição
    Então a API deve responder 400
    E o Console deve deixar claro que isso não é uma decisão de fraude

  Cenário: Nenhum veredito de fraude é exibido
    Dado qualquer envio pelo Console
    Quando o resultado é exibido
    Então em nenhum momento a tela deve apresentar "fraude"/"não fraude" como decisão
    E as cores semânticas devem indicar só a saúde do pipeline, não a faixa de risco

  Cenário: Detalhes técnicos disponíveis sob demanda
    Dado um envio concluído
    Quando expando "detalhes técnicos"
    Então devo ver a requisição e as respostas JSON cruas (POST e GET)

  Cenário: Endpoint de leitura não altera estado
    Dado um caseId existente
    Quando o Console consulta GET /casos/{caseId}
    Então nenhuma escrita, atualização ou remoção deve ocorrer no banco

  Cenário: Acesso compartilhado exige token
    Dado que a API está configurada para acesso compartilhado (não-local)
    Quando alguém acessa o Console ou o GET sem token válido
    Então o acesso deve ser recusado conforme a política de ambiente
```

## 18. KPIs / Critérios de Sucesso

Como ferramenta de operação/QA, o sucesso é medido por adoção e confiabilidade, não por
métricas de negócio:

- A demo e a validação de QA rodam ponta a ponta sem abrir Swagger/logs manualmente.
- Os 5 cenários são reproduzíveis e o ciclo completo aparece em poucos segundos.
- A ferramenta é reusada pela equipe além do workshop (nº de usos em dev/QA).
- A plateia/equipe entende que o retorno é "aceito para análise humana", não veredito.

## 19. Riscos

| Risco | Mitigação |
|---|---|
| `GET /casos/{caseId}` exposto sem autenticação vazaria dados de casos | Autz graduada por ambiente com default seguro (RF15, §13); token obrigatório antes de exposição; alinhar à 2.7. |
| Ler `AntifraudeDbContext` direto na `Api` diverge da convenção de portas | Leitura isolada num único ponto na borda e documentada; scaffolding assumido, a ser substituído pela 2.7. |
| Sobreposição com a Feature 2.7 (Painel do Analista) | Fronteira explícita (§13.1): esta feature é só leitura/observação; o `GET` é candidato a ser absorvido pela 2.7. |
| Faixa "alto" ser lida como veredito na tela | Cores semânticas restritas à saúde do pipeline; faixa/rota/score como priorização neutra; copy não-acusatória (§9.7). |
| Polling sem timeout trava a tela | Timeout obrigatório + corte cedo nos terminais + estado de erro legível (RF08). |
| Ferramenta apontada para ambiente produtivo por engano | Badge de ambiente visível + autz por ambiente barra uso indevido. |
| Design "instrumento" descambar para dashboard genérico | Assinatura única (traço de sinal), boldness concentrada, resto quieto (§9.5). |

## 20. Dependências

- Ambiente existente rodando (`docker compose up`): api + worker + mysql + localstack.
- `POST /sinistros` (Feature 2.1) já implementado.
- `AntifraudeDbContext` acessível a partir da `Api` (já é hoje, no bootstrap).
- (Para acesso compartilhado) valor de token/ambiente definido por configuração.

## 21. Itens Fora do Escopo (desta feature)

- Painel do analista com ações, filas e decisão sobre casos (Feature 2.7).
- Escrita/edição de casos pela interface (só enviar/observar).
- Endpoint de listagem de casos no servidor (histórico é só client-side, da sessão).
- Qualquer cálculo de score/sinais/roteamento (features 2.2–2.5).
- Frontend com framework/build ou dependências externas.
- Decisão de mérito do sinistro; uso voltado ao cliente final / produção externa.
- Integração com IdP real e multiusuário com perfis/permissões ricas.

## 22. Roadmap Futuro

1. **Autz por ambiente** integrada ao provedor de identidade da plataforma, pré-requisito para
   uso compartilhado contínuo.
2. **Convergência com a Feature 2.7:** avaliar mover o `GET /casos/{caseId}` para a capacidade
   da 2.7 e o Console passar a consumi-la.
3. Cadência/timeout do polling configuráveis na tela (hoje fixos ~1s/~20s).
4. Substituir polling por SSE/WebSocket se o uso exigir atualização em tempo real.
5. Exibir a mensagem roteada à fila de erro técnico (quando aplicável), lendo o registro de
   ingestão correspondente.
6. Seletor de ambiente-alvo com sinalização visível, para uso multi-ambiente.

## 23. Glossário

| Termo | Definição |
|---|---|
| **Console de Sinistros** | Página web estática (vanilla), servida pela API, que envia sinistros reais e exibe o ciclo — ferramenta de operação/QA. |
| **Comprovante de ingestão** | Resultado observável do `POST /sinistros`: caseId, idempotência, destino, payload parcial. |
| **Traço de sinal** | Elemento-assinatura: o stepper de pipeline desenhado como uma linha de instrumento por onde o caso viaja. |
| **Mesma origem (same-origin)** | Servir o front da própria API, evitando a necessidade de CORS. |
| **Polling** | Consulta repetida do `GET /casos/{caseId}` até o caso ser processado ou expirar. |
| **Read-only endpoint** | Endpoint que apenas lê estado (SELECT), sem escrever nem decidir. |
| **Autz por ambiente** | Política em que a exigência de autenticação depende do ambiente (livre em local, obrigatória com default seguro em acesso compartilhado). |
| **Aceito para análise humana** | O melhor resultado possível na tela — nunca um veredito de fraude. |

## 24. Registro de decisões (sessão de grilling)

| # | Decisão | Resolução |
|---|---|---|
| 1 | Stack do front | Vanilla estático, sem build, servido de `wwwroot`; frontend-design na qualidade visual à mão. |
| 2 | Acesso à leitura | `DbContext` direto na borda (Api), isolado — scaffolding que a 2.7 substitui. |
| 3 | Escopo de leitura / histórico | Só `GET /casos/{caseId}`; histórico da sessão client-side (sem endpoint de lista). |
| 4 | Modelo de entrada | Formulário estruturado + cenários como chips. |
| 5 | Cenário 400 | Chip dedicado envia corpo cru inválido, com aviso de intencional. |
| 6 | Idempotência dos cenários | "Novo" gera id fresco; "duplicado" reusa o último enviado da sessão. |
| 7 | Visualização do ciclo | Stepper de pipeline (traço de sinal) com estados terminais honestos. |
| 8 | Polling | ~1s, timeout ~20s, corta cedo nos ramos terminais. |
| 9 | Detalhes técnicos | Visão amigável + seção recolhível com JSON cru (POST/GET). |
| 10 | Ambiente/saúde | Badge de ambiente + health check no load. |
| 11 | Direção de design | Painel de instrumento de backoffice; semântica de status separada do acento; mono como display; claro/escuro. |
| 12 | Não-acusação | Estado em destaque ("aceito → análise humana"); faixa/rota/score como priorização neutra. |
| 13 | Autz | Flag por ambiente + token simples, default seguro; IdP real adiado até a 2.7. |
