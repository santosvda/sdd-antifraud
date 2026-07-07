## Why

A fundação (walking skeleton) recebe um contrato-placeholder (`POST /sinistros` com
`sinais[]` já prontos) — mas o sinistro real da Trilha B (sinistro por foto) não chega com
sinais calculados: chega com dados brutos (apólice, aparelho, fotos, metadados) e os sinais
são responsabilidade das features seguintes (2.2 em diante). Esta change materializa a
**Feature 2.1 — Ingestão do Sinistro** (PRD em
[`docs/features/feature-2-1-ingestao-sinistro/prd.md`](../../../docs/features/feature-2-1-ingestao-sinistro/prd.md)):
transforma a borda de entrada no ponto de ingestão real — idempotente, resiliente e
non-blocking — para que o cliente honesto nunca espere o antifraude e nenhum evento se
perca em silêncio.

## What Changes

- **BREAKING — contrato de entrada:** o corpo do `POST /sinistros` deixa de ser `{ sinais[] }`
  e passa a ser o **payload de sinistro real**: `idSinistro` (único campo estrutural),
  `apolice`, `aparelho` (IMEI/série), `fotos` (por referência — ID/URL, nunca cópia),
  `metadados` (data/hora de abertura, canal, idCliente). Os sinais saem da ingestão (viram
  responsabilidade da 2.2).
- **Idempotência:** o mesmo `idSinistro` nunca gera duas entradas na fila de processamento.
  Store de deduplicação com **TTL de 24h**; se o store estiver indisponível, **fail-open**
  (processa mesmo assim + alerta + reconciliação posterior), nunca bloqueia por causa da
  checagem.
- **Payload parcial:** ausência de qualquer campo **exceto** o `idSinistro` não bloqueia — o
  caso segue enfileirado marcado como `payloadParcial`, para as features downstream tratarem
  a ausência do dado.
- **Fila de erro técnico (DLQ):** evento sem `idSinistro` é **não-processável** → roteado
  para a fila de erro técnico com alerta operacional, **sem devolver erro ao produtor** (o
  sinistro já existe no sistema principal). Regra do `400` encolhe: só corpo ilegível
  (JSON inválido) retorna `400`; JSON bem-formado sem `idSinistro` retorna `202` e vai para a
  DLQ.
- **Resiliência no enfileiramento:** falha transitória de enfileiramento aciona retry com
  backoff exponencial (**3 tentativas, ~1s/4s/16s**); se persistir, escala para a DLQ com
  alerta — nunca descarta silenciosamente.
- **Auditoria da ingestão:** cada evento recebido gera registro imutável de completude
  (campos presentes/ausentes), resultado da idempotência (primeira vez / duplicado
  descartado) e destino do roteamento (fila normal vs. erro técnico).
- **Entrada mantida em HTTP:** `POST /sinistros` continua sendo o transporte (decisão de
  arquitetura); trocar para um event bus no futuro é um adapter de borda que não toca o
  `Core`.

## Capabilities

### New Capabilities

- `sinistro-idempotency`: deduplicação por `idSinistro` com TTL de 24h, garantindo que
  reentregas/retries do produtor dentro da janela não gerem processamento duplicado;
  fail-open explícito quando o store de dedup está indisponível.
- `ingestion-error-handling`: fila de erro técnico (DLQ) para eventos não-processáveis
  (sem `idSinistro`) e retry com backoff exponencial no enfileiramento, com alerta
  operacional e sem perda silenciosa de eventos.

### Modified Capabilities

- `claim-intake-api`: o payload de entrada passa a ser o sinistro real (não mais `sinais[]`);
  a regra de rejeição na borda muda (só corpo ilegível → `400`; JSON válido sem `idSinistro`
  → `202` + DLQ); a API passa a marcar `payloadParcial` e a aplicar a checagem de
  idempotência antes de enfileirar.
- `claim-processing-worker`: consome o novo contrato de mensagem (sinistro real +
  `payloadParcial`); como os sinais ainda não são computados (2.2 é change futura), o
  fail-open existente roteia esses casos para `PendenteRevisaoManual` — o fluxo ponta a ponta
  segue funcionando.
- `immutable-audit-trail`: passa a registrar também a auditoria da própria ingestão
  (completude do payload, resultado da idempotência, destino do roteamento), além da
  auditoria de decisão já existente.

## Impact

- **Código:**
  - `Antifraude.Core` — entidade `Sinistro` remodelada para o domínio real (idSinistro,
    apólice, aparelho, fotos por referência, metadados, flag de payload parcial); nova porta
    para o store de idempotência.
  - `Antifraude.Api` — `SinistroRequest`/validação da borda reescritas; roteamento de
    malformado para DLQ; checagem de idempotência.
  - `Antifraude.Infra` — adapter do store de dedup (tabela MySQL com TTL lógico de 24h, sem
    novo componente de infra), publicação na DLQ, retry/backoff no `SqsSinistroQueue`,
    registro de auditoria de ingestão.
  - `Antifraude.Worker` — desserializa o novo contrato; fail-open para casos sem sinais.
- **Infra/Docker:** segunda fila SQS (erro técnico) garantida no bootstrap junto da fila
  principal, no LocalStack — sem serviço novo no `compose.yaml`.
- **Contrato/API:** breaking no corpo do `POST /sinistros`; Swagger atualizado.
- **Specs:** 2 novos (`sinistro-idempotency`, `ingestion-error-handling`) + 3 delta
  (`claim-intake-api`, `claim-processing-worker`, `immutable-audit-trail`).
- **Testes:** unit (Core: idempotência, payload parcial, fail-open) + integração
  (Testcontainers: duplicado descartado, sem-ID → DLQ, retry→escalonamento, auditoria de
  ingestão imutável).
- **Guardrails:** reforça non-blocking, fail-open e auditoria imutável; nenhum guardrail é
  relaxado.
