## Context

A fundação hoje trafega `Sinistro(CaseId, Sinais[])` da API pela fila até o Worker; a API
valida formato e enfileira, o Worker roda o `MotorDeDecisao` (que já faz **fail-open** para
`PendenteRevisaoManual` quando `SinaisIncompletos`). Não há idempotência, fila de erro
técnico nem auditoria da própria ingestão. O `IScoreProvider` é mock sinalizado e a 2.2
(coleta de sinais) ainda não existe.

Esta change materializa a Feature 2.1: substitui o contrato-placeholder pelo **payload de
sinistro real** e adiciona idempotência, roteamento de erro técnico e auditoria de ingestão —
mantendo a decisão de arquitetura de **manter a entrada em HTTP `POST /sinistros`** (o
Sistema de Sinistros entrega o evento via POST que responde `202`). Restrições herdadas:
`Core` sem EF/SQS; composição na borda via `AddAntifraudeInfra`; guardrails inegociáveis
(non-blocking, fail-open, auditoria imutável, config governada).

## Goals / Non-Goals

**Goals:**
- Contrato de entrada real: `idSinistro` (estrutural) + apólice, aparelho, fotos (por
  referência), metadados; sinais saem da ingestão.
- Idempotência por `idSinistro` (TTL 24h) com fail-open quando o store cai.
- Fila de erro técnico para eventos sem `idSinistro` + retry/backoff no enfileiramento.
- Auditoria imutável da ingestão (completude, idempotência, roteamento).
- Fluxo ponta a ponta segue rodando: sem sinais computados, os casos caem em
  `PendenteRevisaoManual` (fail-open já existente).
- Zero serviço novo no `compose.yaml` (reusar MySQL + LocalStack).

**Non-Goals:**
- Coleta/cálculo de sinais (2.2), score determinístico real (2.3), roteamento por risco (2.5).
- Migrar a entrada para event bus (decisão explícita: fica em HTTP; bus é adapter futuro).
- Painel operacional / UI de saúde da ingestão (exposição de métricas apenas).

## Decisions

### D1 — Remodelar `Sinistro` mantendo `Sinais` como costura downstream
`Sinistro` ganha `IdSinistro` (string, único estrutural), `Apolice`, `Aparelho` (IMEI/série),
`Fotos` (lista de referências ID/URL), `Metadados` (abertura, canal, idCliente) e
`PayloadParcial` (bool). **`Sinais` permanece** (default vazio) como o ponto onde a 2.2 vai
injetar os sinais no futuro.
- *Por quê:* o `MotorDeDecisao` já trata `Sinais` vazio via fail-open → `PendenteRevisaoManual`.
  Manter o campo evita churn e deixa o fluxo íntegro sem a 2.2.
- *Alternativa:* remover `Sinais` agora e reintroduzir na 2.2 — rejeitada (mais churn no Core,
  motor e auditoria, sem ganho nesta fatia).

### D2 — Idempotência na borda (API), store em tabela MySQL com TTL lógico de 24h
Nova porta `Core` (ex.: `ISinistroDedupStore`) com adapter EF em `Infra`, tabela
`sinistros_processados` (`id_sinistro` PK, `primeira_vez_em`). Checagem: "visto" só se houver
linha com `primeira_vez_em > agora − 24h". A API checa **antes** de enfileirar; duplicado →
log + auditoria + `202` sem nova entrada na fila.
- *Por quê:* reusa o MySQL que a API já acessa; zero infra nova; TTL de 24h cobre reentrega
  do produtor.
- *Alternativa:* Redis com TTL nativo — rejeitada nesta fatia (novo container/serviço). Fica
  no roadmap se volume exigir.
- *Limpeza:* purga de linhas > 24h (no start + varredura periódica leve) para o crescimento
  não ficar ilimitado.

### D3 — Fail-open explícito da checagem de idempotência
Exceção ao consultar/gravar o store → processa o evento normalmente, emite alerta técnico e
registra para reconciliação; nunca bloqueia por causa da dedup.
- *Por quê:* guardrail fail-open; duplicidade ocasional é preferível a perder o sinistro.

### D4 — Fila de erro técnico como segunda fila SQS explícita (aplicacional)
Fila `sinistros-erro-tecnico` no LocalStack, garantida no bootstrap junto da principal.
Recebe: (a) eventos bem-formados sem `idSinistro`; (b) eventos escalados após esgotar o retry
de enfileiramento. Sempre com alerta operacional.
- *Por quê:* a "fila de erro técnico" do PRD é **aplicacional** (evento não-processável +
  escalonamento), não um DLQ de entrega — um canal explícito é mais claro e testável.
- *Alternativa:* redrive policy nativa do SQS — rejeitada (semântica de entrega, não cobre
  "sem idSinistro" nem escalonamento pós-retry).

### D5 — Retry/backoff síncrono só no caminho de falha; `202` imediato no happy path
Publicação no SQS tenta uma vez; em falha transitória, retry com backoff (~1s/4s/16s, 3x);
se persistir, publica na fila de erro técnico. O happy path retorna `202` em milissegundos.
- *Por quê:* o "cliente" desta borda é o **Sistema de Sinistros** (backend), e o sinistro do
  cliente final já foi aberto no sistema principal — logo, o pior caso (~21s) no caminho raro
  de falha não atrasa o cliente final. Mantém o modelo síncrono simples da fundação.
- *Alternativa:* despacho em background (canal in-process) para nunca bloquear o HTTP —
  rejeitada nesta fatia (introduz lacuna de durabilidade in-process); fica no roadmap.

### D6 — Auditoria de ingestão em tabela dedicada, mesma disciplina imutável
Nova tabela `auditoria_ingestao` (`id_sinistro`, `recebido_em`, campos presentes/ausentes,
`payload_parcial`, resultado da idempotência, destino do roteamento) com triggers
`BEFORE UPDATE`/`BEFORE DELETE` idênticos aos da `auditoria`. Escrita na borda (API).
- *Por quê:* schema da ingestão é diferente do carimbo de decisão; tabela própria evita
  poluir `auditoria` com colunas nuláveis, preservando a mesma **disciplina append-only
  imutável**.
- *Alternativa:* uma única tabela com discriminador `tipo` — rejeitada (schema confuso,
  muitos campos nuláveis).

### D7 — Contrato SQS e Worker
A mensagem SQS passa a ser o `Sinistro` remodelado (campos reais + `Sinais` vazio +
`PayloadParcial`). O Worker desserializa o mesmo record; `PayloadParcial` é propagado para
`Caso`/auditoria (distinto de `DadosIncompletos`, que é a dimensão de sinais). Sem sinais →
`MotorDeDecisao` mantém `PendenteRevisaoManual`.

## Risks / Trade-offs

- **Broker SQS totalmente indisponível** (principal *e* erro técnico) → não há para onde
  rotear. → *Decisão:* log crítico + a API responde `503` **apenas** neste caso extremo,
  para o Sistema de Sinistros reenviar (exceção deliberada ao "nunca sinalizar o produtor":
  é indisponibilidade de infra, não decisão de mérito).
- **Crescimento da tabela de dedup** com TTL lógico. → *Mitigação:* purga periódica de linhas
  > 24h; índice em `primeira_vez_em`.
- **Retry síncrono (~21s) bloqueia o HTTP no caminho de falha.** → *Mitigação:* aceitável
  (caller é backend; cliente final não afetado); background dispatch no roadmap.
- **`payloadParcial` vs `dadosIncompletos` confundíveis.** → *Mitigação:* campos e semânticas
  distintas, documentadas (parcial = campos de intake ausentes; incompletos = sinais
  ausentes para decisão).
- **BREAKING no corpo do `POST /sinistros`.** → *Mitigação:* ambiente pré-produção (workshop);
  atualizar Swagger e scripts de smoke; sem consumidores externos a migrar.

## Migration Plan

1. Novas migrations EF (aplicadas no start da API, mecanismo existente):
   `sinistros_processados` (dedup); `auditoria_ingestao` + triggers de imutabilidade; ajuste
   de `casos` para `payload_parcial`.
2. Bootstrap passa a garantir **duas** filas SQS (principal + erro técnico).
3. Trocar o contrato/DTO da API + validação; atualizar Swagger.
4. Worker: desserializar o novo `Sinistro`; propagar `PayloadParcial`.
5. **Rollback:** reverter migrations (drop das tabelas novas + coluna), reverter código; como
   são tabelas majoritariamente aditivas, o rollback é direto e sem perda de estado legado.

## Open Questions (resolvidas)

- **Indisponibilidade total do broker** → **`503` ao produtor** + log crítico. O Sistema de
  Sinistros reenvia; exceção deliberada ao "nunca sinalizar o produtor" por ser falha de
  infra (não decisão de mérito), sem perda de evento. *(Resolvido.)*
- **Purga do store de dedup** → **`IHostedService` agendado** (varredura periódica de linhas
  > 24h), mantendo a tabela enxuta continuamente. *(Resolvido.)*
- **Auditoria de ingestão** → **tabela dedicada** `auditoria_ingestao` com os mesmos triggers
  de imutabilidade; "na mesma trilha" interpretado como "mesma disciplina append-only
  imutável". *(Resolvido.)*
