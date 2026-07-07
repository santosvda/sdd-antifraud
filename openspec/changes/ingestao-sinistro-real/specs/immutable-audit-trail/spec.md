## ADDED Requirements

### Requirement: Auditoria imutável da ingestão

Para cada evento de sinistro recebido, o sistema SHALL registrar na mesma trilha append-only
(protegida pelo trigger que bloqueia UPDATE e DELETE) um carimbo de ingestão contendo:
`idSinistro`, timestamp de recepção, quais campos do payload mínimo estavam presentes ou
ausentes (com a flag `payloadParcial` quando aplicável), o resultado da checagem de
idempotência (primeira vez / duplicado descartado) e o destino do roteamento (fila de
processamento vs. fila de erro técnico). Esse registro MUST ser imutável como os demais
registros de auditoria.

#### Scenario: Evento recebido gera carimbo de completude

- **WHEN** a ingestão processa um evento de sinistro
- **THEN** existe um registro imutável com `idSinistro`, timestamp, campos presentes/ausentes,
  resultado da idempotência e destino do roteamento

#### Scenario: Evento duplicado descartado é auditado

- **WHEN** um evento é descartado pela checagem de idempotência
- **THEN** a trilha registra o descarte como duplicado, de forma imutável

#### Scenario: Roteamento para fila de erro técnico é auditado

- **WHEN** um evento sem `idSinistro` é roteado para a fila de erro técnico
- **THEN** a trilha registra o destino do roteamento, de forma imutável
