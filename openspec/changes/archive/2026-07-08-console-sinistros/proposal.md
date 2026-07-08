## Why

O Motor Antifraude roda ponta a ponta (`POST /sinistros → SQS → Worker → MySQL`), mas **nenhum cliente o consome**: exercitá-lo hoje exige Swagger/`curl` para enviar e depois vasculhar logs/banco para ver o caso — frágil ao vivo, lento para QA, e não conta a história dos guardrails. Esta mudança entrega um **Console de Sinistros**: um cliente web servido pela própria API que dispara sinistros reais e mostra o ciclo assíncrono do caso na tela, honrando o guardrail de que o resultado é "aceito para análise humana", nunca um veredito. Detalhes de produto/design no PRD: `docs/features/feature-console-sinistros-demo/prd.md`.

## What Changes

- **Novo endpoint read-only `GET /casos/{caseId}`** na API: retorna o caso (estado, faixa, rota, score, versões, `payloadParcial`, `criadoEm`), a trilha de ingestão e a trilha de decisão correlacionadas — ou "não encontrado" enquanto o Worker não processou. Só SELECT; nunca escreve nem decide.
- **Console web estático (vanilla, sem build)** servido de `wwwroot` pela própria API (mesma origem, sem CORS): formulário estruturado + cenários pré-montados (novo, duplicado, parcial, sem `idSinistro`, corpo ilegível), stepper de pipeline ("traço de sinal") alimentado por polling, histórico da sessão (client-side), seção recolhível de detalhes técnicos (JSON cru), badge de ambiente + health check.
- **Não-acusação na UI**: estado ("aceito → análise humana") em destaque; faixa/rota/score como priorização neutra; cores semânticas restritas à saúde do pipeline, nunca à faixa de risco.
- **Autz graduada por ambiente (default seguro)**: Console + `GET` liberados em local/dev; exigem token simples quando expostos; desabilitados por padrão se o modo não-local não for configurado.
- **Sem alteração de comportamento** no domínio (`Core`), no `POST /sinistros`, no Worker ou no `Dockerfile.api` (o `wwwroot` entra no publish por padrão).

## Capabilities

### New Capabilities
- `case-read-api`: endpoint HTTP somente-leitura para consultar um caso e suas trilhas de auditoria (ingestão + decisão) por `caseId`, com garantia de não-escrita/não-decisão e acesso governado por ambiente.
- `claim-console`: cliente web de operação/QA servido pela própria API, que envia sinistros reais e exibe o ciclo do caso (comprovante → stepper → caso + auditoria), com cenários reproduzíveis, não-acusação na apresentação e autz graduada por ambiente.

### Modified Capabilities
<!-- Nenhuma. O Console consome POST /sinistros sem alterar seu comportamento; servir estáticos é requisito da capacidade claim-console, não mudança de requisito da claim-intake-api. -->

## Impact

- **Código:** `src/Antifraude.Api` — novo endpoint `GET /casos/{caseId}` (lê `AntifraudeDbContext` isolado na borda; SELECT em `casos`, `auditoria`, `auditoria_ingestao`), `UseDefaultFiles()`/`UseStaticFiles()`, exposição mínima de ambiente/saúde, e o gate de autz por ambiente. Novos assets em `src/Antifraude.Api/wwwroot/` (`index.html`, `styles.css`, `app.js`, fonte mono auto-hospedada).
- **Domínio:** `Core` intocado; nenhuma porta nova (a leitura fica na borda como scaffolding, candidato a ser absorvido pela Feature 2.7).
- **Config:** flag/token de ambiente para o gate de acesso (via env var; `.env.example` atualizado).
- **Infra:** sem novo container, sem CORS, sem mudança no `Dockerfile.api`. Sobe no `docker compose up` existente.
- **Testes:** integração do `GET /casos/{caseId}` (caso encontrado, ainda-não-processado, inexistente, read-only) e do gate de acesso por ambiente.
- **Fronteira:** a `case-read-api` faz par/limite com a futura Feature 2.7 (Painel do Analista), que fará a leitura rica de casos.
