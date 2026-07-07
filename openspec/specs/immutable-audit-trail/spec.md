# immutable-audit-trail Specification

## Purpose

Trilha de auditoria imutável: tabela append-only no MySQL protegida por trigger que bloqueia UPDATE e DELETE, com um carimbo de decisão completo e rastreável por caso (sinais, score, faixa, versões de config e provider, rota, timestamp e ator), correlacionado pelo `caseId`.

## Requirements

### Requirement: Trilha de auditoria append-only imutável

O sistema SHALL manter a trilha de auditoria em uma tabela append-only no MySQL, protegida por um **trigger que bloqueia UPDATE e DELETE**. Qualquer tentativa de alterar ou remover um registro de auditoria MUST falhar com erro do banco.

#### Scenario: UPDATE em registro de auditoria é bloqueado

- **WHEN** um `UPDATE` é executado contra uma linha da tabela de auditoria
- **THEN** o banco dispara erro e a linha permanece inalterada

#### Scenario: DELETE em registro de auditoria é bloqueado

- **WHEN** um `DELETE` é executado contra uma linha da tabela de auditoria
- **THEN** o banco dispara erro e a linha permanece presente

### Requirement: Carimbo de decisão completo por caso

Cada caso processado SHALL registrar na auditoria: os sinais recebidos e sua origem, o score, a faixa, a **versão da `scoring_config`** vigente, a **versão do `IScoreProvider`**, a rota atribuída, o timestamp e o ator. O `caseId` MUST correlacionar o registro de auditoria ao caso e à requisição de origem.

#### Scenario: Caso processado gera carimbo rastreável

- **WHEN** o Worker finaliza o processamento de um sinistro
- **THEN** existe um registro de auditoria com sinais+origem, score, faixa, versão da config, versão do provider, rota, timestamp e ator, todos correlacionados pelo `caseId`

#### Scenario: Falha do provider também é auditada

- **WHEN** o `IScoreProvider` falha e o caso nasce como `PENDENTE_REVISAO_MANUAL`
- **THEN** a auditoria registra a falha e a ausência de score, mantendo o caso visível e rastreável
