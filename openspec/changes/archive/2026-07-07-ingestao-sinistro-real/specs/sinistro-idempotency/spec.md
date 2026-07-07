## ADDED Requirements

### Requirement: Deduplicação por idSinistro com janela de 24h

O sistema SHALL garantir que o mesmo `idSinistro` nunca gere duas entradas na fila de
processamento. A checagem de idempotência MUST usar um store de deduplicação que retém o
`idSinistro` por **24 horas (TTL)**, cobrindo janelas típicas de retry/reentrega do Sistema
de Sinistros. Eventos duplicados dentro da janela MUST ser descartados com log, sem gerar
novo processamento.

#### Scenario: Primeiro evento de um idSinistro é processado

- **WHEN** um evento com um `idSinistro` ainda não visto é recebido
- **THEN** o `idSinistro` é registrado no store de deduplicação e o caso segue para
  enfileiramento

#### Scenario: Evento duplicado dentro de 24h é descartado

- **WHEN** um evento com um `idSinistro` já processado nas últimas 24 horas é recebido
  novamente
- **THEN** o evento é descartado com log e nenhuma nova entrada é criada na fila de
  processamento

### Requirement: Fail-open quando o store de deduplicação está indisponível

Quando o store de deduplicação está indisponível, o sistema SHALL processar o evento
normalmente (fail-open desta checagem específica), emitindo alerta técnico e registro para
reconciliação posterior. A indisponibilidade da checagem de duplicidade MUST NOT bloquear o
enfileiramento do sinistro.

#### Scenario: Store de dedup fora do ar não bloqueia o sinistro

- **WHEN** o store de deduplicação está inacessível ao processar um evento
- **THEN** o evento é enfileirado normalmente, um alerta técnico é emitido e o caso fica
  registrado para reconciliação posterior — o sinistro nunca é bloqueado pela checagem
