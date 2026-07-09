## MODIFIED Requirements

### Requirement: Carimbo de decisão completo por caso

Cada caso processado SHALL registrar na auditoria: os sinais recebidos e sua origem, o score, a faixa, a **versão da `scoring_config`** vigente, a **versão do `IScoreProvider`**, a rota atribuída, a indicação de **cobertura parcial** (quando um sinal esteve ausente e os pesos foram renormalizados), o timestamp e o ator. Quando um atributo proibido for filtrado da entrada, a auditoria SHALL registrar o **evento de conformidade** correspondente. O `caseId` MUST correlacionar o registro de auditoria ao caso e à requisição de origem. O carimbo MUST NOT conter atributos sensíveis proibidos.

#### Scenario: Caso processado gera carimbo rastreável

- **WHEN** o Worker finaliza o processamento de um sinistro
- **THEN** existe um registro de auditoria com sinais+origem, score, faixa, versão da config, versão do provider, rota, indicação de cobertura parcial, timestamp e ator, todos correlacionados pelo `caseId`

#### Scenario: Falha do provider também é auditada

- **WHEN** o `IScoreProvider` falha e o caso nasce como `PENDENTE_REVISAO_MANUAL`
- **THEN** a auditoria registra a falha e a ausência de score, mantendo o caso visível e rastreável

#### Scenario: Cobertura parcial é carimbada

- **WHEN** o caso é pontuado com apenas 2 dos 3 sinais (pesos renormalizados)
- **THEN** a auditoria registra a indicação de cobertura parcial junto do score, faixa e versão da config

#### Scenario: Atributo proibido filtrado gera evento de conformidade

- **WHEN** um atributo sensível proibido é filtrado da entrada de sinais antes do cálculo
- **THEN** a auditoria registra o evento de conformidade, sem incluir o atributo proibido no carimbo
