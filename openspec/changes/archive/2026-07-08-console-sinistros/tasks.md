## 1. Endpoint de leitura (`case-read-api`)

- [x] 1.1 Adicionar `GET /casos/{caseId:guid}` na `Antifraude.Api` injetando `AntifraudeDbContext`, com 3 SELECT (`casos`, `auditoria_ingestao`, `auditoria`) por `caseId`, isolado num único handler.
- [x] 1.2 Projetar a resposta para DTO anônimo com enums (`Estado`/`Faixa`/`Rota`/`Idempotencia`/`Destino`) como string via `.ToString()`.
- [x] 1.3 Implementar os 3 estados: `200 encontrado:true` (caso + trilhas), `200 encontrado:false` (só trilha de ingestão, Worker ainda não gravou), `404` (nada).
- [x] 1.4 Garantir somente-leitura (apenas SELECT; nenhuma escrita/atualização/remoção) e ausência de campo de veredito na resposta.

## 2. Servir o Console estático

- [x] 2.1 Adicionar `app.UseDefaultFiles()` + `app.UseStaticFiles()` na `Program.cs`, antes de Swagger/endpoints, servindo `wwwroot` na raiz `/`.
- [x] 2.2 Criar o esqueleto de `src/Antifraude.Api/wwwroot/` (`index.html`, `styles.css`, `app.js`, `fonts/`).
- [x] 2.3 Confirmar que `wwwroot` entra no `dotnet publish` (imagem Docker) sem alterar `Dockerfile.api`.

## 3. Ambiente e saúde

- [x] 3.1 Estender `GET /health` para retornar `{ status, ambiente }` (config com fallback `IHostEnvironment.EnvironmentName`).
- [x] 3.2 No Console, chamar `/health` no load e exibir indicador de conectividade + badge de ambiente; aviso claro se a API estiver inacessível.

## 4. Gate de acesso graduado por ambiente

- [x] 4.1 Ler configuração do gate (`Console__Modo` = local|compartilhado|desabilitado, `Console__Credenciais`) e default seguro (desabilitado fora de `Development` quando não configurado).
- [x] 4.2 Aplicar o gate ao Console e ao `GET /casos` (e `/health` em modo compartilhado): `local` libera; `compartilhado` exige Basic auth; não configurado → Console 404 / endpoint 403.
- [x] 4.3 Atualizar `.env.example` com as chaves do gate (default local) — nunca commitar `.env`.

## 5. Front — estrutura de design (PRD §9)

- [x] 5.1 Definir tokens de cor (claro/escuro) como CSS custom properties, com `@media (prefers-color-scheme)` + override `data-theme`; acento índigo-violeta e semânticas restritas à saúde do pipeline.
- [x] 5.2 Auto-hospedar a face mono (`@font-face` local, sem CDN) como display/dados; corpo em stack de sistema; escala de tipos + `tabular-nums`.
- [x] 5.3 Montar o layout de dois painéis (Controle × Readout) + barra de instrumento (título, badge, saúde, toggle de tema); responsivo (empilha no mobile), foco visível, sem scroll horizontal.

## 6. Front — envio e cenários

- [x] 6.1 Construir o formulário estruturado (`idSinistro`, `apolice`, `aparelho.imei/numeroSerie`, `fotos[]` por referência, `metadados.*`).
- [x] 6.2 Implementar os 5 chips de cenário que preenchem o formulário (completo/novo, duplicado, parcial, sem idSinistro, corpo ilegível).
- [x] 6.3 Idempotência reproduzível: "novo" gera `idSinistro` fresco; "duplicado" reusa `ultimoIdEnviado` da sessão (com dica se não houver envio prévio).
- [x] 6.4 Enviar `POST /sinistros` real (same-origin) e derivar o comprovante (202/400/503 + `caseId`); "corpo ilegível" envia corpo cru malformado com aviso de intencional.

## 7. Front — stepper, polling e apresentação

- [x] 7.1 Implementar o stepper "traço de sinal" (4 estágios) com o pulso animado e supressão sob `prefers-reduced-motion`.
- [x] 7.2 Polling `GET /casos/{caseId}` (~1s, timeout ~20s) atualizando o stepper; parar cedo nos ramos terminais (Descartado / Fila de erro técnico) lendo o `destino`.
- [x] 7.3 Apresentar o caso sem veredito: estado em destaque; faixa/rota/score como priorização neutra; nenhuma cor semântica na faixa; timeout mostra "Worker ainda processando".
- [x] 7.4 Histórico da sessão (em memória) com reabertura do polling por `caseId`.
- [x] 7.5 Seção recolhível de detalhes técnicos (método/URL, status, corpos JSON crus de POST e GET).

## 8. Testes e verificação

- [x] 8.1 Testes de integração do `GET /casos/{caseId}` (Testcontainers): processado, recebido-mas-não-processado, inexistente (404), e read-only (nenhuma escrita).
- [x] 8.2 Testes do gate de acesso (local libera; compartilhado sem credencial → 401/403; desabilitado → 404).
- [x] 8.3 Verificação manual dos 5 cenários contra os critérios Gherkin dos specs; rodar `dotnet format` e a suíte antes de concluir.
