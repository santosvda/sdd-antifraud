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

### Requirement: Evidência imutável por sinal coletado

Para cada caso processado, a trilha de auditoria SHALL registrar, por sinal coletado, de
forma imutável (mesma tabela append-only protegida por trigger): o estado do sinal
(ativo / inativo / indisponível), a evidência específica que motivou o valor (ex.:
sinistro colidido e distância de Hamming para reuso de imagem; identificadores comparados
— mascarados — para IMEI×série; contagem e janela para velocity), o motivo da
indisponibilidade quando aplicável (dado ausente × fonte externa inacessível), a origem
do cálculo (ex.: `phash-fake-v1`) e o timestamp do cálculo. Identificadores sensíveis
(IMEI/série) MUST aparecer mascarados na evidência.

#### Scenario: Auditoria registra evidência de cada sinal

- **WHEN** o processamento de um caso pela coleta de sinais é concluído
- **THEN** o registro de auditoria contém, para cada um dos 3 sinais, o estado, a
  evidência, a origem, o timestamp e o motivo de eventual indisponibilidade — e esse
  registro é imutável (UPDATE/DELETE bloqueados)

#### Scenario: Evidência mascara identificadores sensíveis

- **WHEN** a evidência do sinal `imei_serie_divergente` é registrada
- **THEN** IMEI e número de série aparecem truncados/mascarados, nunca in-the-clear
