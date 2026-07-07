## 1. Domínio (Core)

- [ ] 1.1 Remodelar o record `Sinistro`: adicionar `IdSinistro` (string, estrutural), `Apolice`, `Aparelho` (IMEI/série), `Fotos` (lista de referências ID/URL), `Metadados` (abertura, canal, idCliente) e `PayloadParcial` (bool); manter `Sinais` com default vazio (costura para a 2.2) — D1
- [ ] 1.2 Criar a porta `ISinistroDedupStore` (ex.: `JaVistoAsync`/`RegistrarAsync` por `idSinistro`) em `Core/Portas`, sem qualquer dependência de EF/SQS — D2
- [ ] 1.3 Criar a porta de auditoria de ingestão (ex.: `IAuditLogIngestao`) e o registro de domínio correspondente (idSinistro, recebidoEm, campos presentes/ausentes, payloadParcial, resultado idempotência, destino roteamento) — D6
- [ ] 1.4 Propagar `PayloadParcial` para `Caso`/`RegistroAuditoria` como dimensão distinta de `DadosIncompletos` — D7
- [ ] 1.5 Testes unitários do Core: `Sinistro` com só `idSinistro` → `PayloadParcial=true`; sem `idSinistro` → não-processável; motor com `Sinais` vazio segue caindo em `PendenteRevisaoManual`

## 2. Persistência e mensageria (Infra)

- [ ] 2.1 Adapter EF `ISinistroDedupStore`: tabela `sinistros_processados` (`id_sinistro` PK, `primeira_vez_em`); "visto" só se `primeira_vez_em > agora − 24h`; fail-open (exceção → processa + alerta) — D2, D3
- [ ] 2.2 Migration `SinistrosProcessados` (dedup) com índice em `primeira_vez_em`
- [ ] 2.3 Adapter EF `IAuditLogIngestao`: tabela `auditoria_ingestao` + triggers `BEFORE UPDATE`/`BEFORE DELETE` (SIGNAL SQLSTATE), espelhando a imutabilidade da `auditoria` — D6
- [ ] 2.4 Migration `AuditoriaIngestaoImutavel` (tabela + triggers)
- [ ] 2.5 Segunda fila SQS `sinistros-erro-tecnico`: estender `SqsOptions`/`ISinistroQueue` (ou nova porta) para publicar na fila de erro técnico — D4
- [ ] 2.6 Retry com backoff (~1s/4s/16s, 3x) na publicação do `SqsSinistroQueue`; ao esgotar, publica na fila de erro técnico com alerta — D5
- [ ] 2.7 Registrar as novas portas/tabelas/filas no `AddAntifraudeInfra` (DI) e no `AntifraudeDbContext`
- [ ] 2.8 `IHostedService` agendado que purga linhas de dedup > 24h periodicamente — D2

## 3. Borda de entrada (Api)

- [ ] 3.1 Reescrever `SinistroRequest` para o payload real e mapear para o domínio (`ParaDominio` com `idSinistro`, apólice, aparelho, fotos por referência, metadados)
- [ ] 3.2 Validação de borda: corpo ilegível → `400`; JSON válido sem `idSinistro` → `202` + roteia para fila de erro técnico (nunca `400`) — claim-intake-api, ingestion-error-handling
- [ ] 3.3 Marcar `PayloadParcial` quando faltar campo não-estrutural e enfileirar mesmo assim (`202`) — claim-intake-api
- [ ] 3.4 Checagem de idempotência antes de enfileirar: duplicado em 24h → log + auditoria + `202` sem nova entrada na fila — sinistro-idempotency
- [ ] 3.5 Registrar auditoria de ingestão em todos os ramos (completude, resultado idempotência, destino do roteamento) — immutable-audit-trail
- [ ] 3.6 Tratamento de indisponibilidade total do broker: `503` ao produtor + log crítico (decisão firmada)
- [ ] 3.7 Atualizar Swagger/OpenAPI com o novo contrato de `POST /sinistros`

## 4. Processamento (Worker)

- [ ] 4.1 Desserializar o novo `Sinistro` (campos reais + `Sinais` vazio + `PayloadParcial`) — sem sinais → `MotorDeDecisao` mantém `PendenteRevisaoManual` — claim-processing-worker
- [ ] 4.2 Propagar/auditar `PayloadParcial` no caso persistido

## 5. Ambiente (Docker/bootstrap)

- [ ] 5.1 Garantir as **duas** filas SQS (principal + erro técnico) no bootstrap, no LocalStack — sem serviço novo no `compose.yaml`
- [ ] 5.2 Confirmar que as novas migrations aplicam no start da API antes do healthy

## 6. Testes

- [ ] 6.1 Integração (Testcontainers): evento duplicado em 24h é descartado, sem segunda entrada na fila — sinistro-idempotency
- [ ] 6.2 Integração: store de dedup fora do ar → evento processado (fail-open) + alerta — sinistro-idempotency
- [ ] 6.3 Integração: JSON válido sem `idSinistro` → `202` + mensagem na fila de erro técnico — ingestion-error-handling / claim-intake-api
- [ ] 6.4 Integração: falha transitória de enfileiramento → retry supera; falha persistente → escala para fila de erro técnico — ingestion-error-handling
- [ ] 6.5 Integração: payload parcial (sem aparelho) → `202` + caso marcado `payloadParcial` — claim-intake-api
- [ ] 6.6 Integração: `UPDATE`/`DELETE` em `auditoria_ingestao` disparam erro (imutabilidade) — immutable-audit-trail
- [ ] 6.7 Integração (fumaça): `POST /sinistros` real → sem sinais → caso `PENDENTE_REVISAO_MANUAL` persistido — claim-processing-worker
- [ ] 6.8 Unit: validação de borda (ilegível vs sem-id vs parcial vs completo)

## 7. Documentação e fechamento

- [ ] 7.1 Atualizar `ARCHITECTURE.md`: novo contrato de entrada, idempotência, fila de erro técnico, auditoria de ingestão
- [ ] 7.2 `dotnet format` sem erros e rodar `/simplify` antes de concluir
- [ ] 7.3 Confirmar no `ARCHITECTURE.md`/design que as decisões firmadas foram implementadas (broker down → 503, purga via IHostedService, auditoria_ingestao dedicada)
