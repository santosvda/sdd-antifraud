## MODIFIED Requirements

### Requirement: Carimbo de decisão completo por caso

Cada caso processado SHALL registrar na auditoria: os sinais recebidos e sua origem, o score, a faixa, a **versão da `scoring_config`** vigente, a **versão do `IScoreProvider`**, a rota atribuída, a indicação de **cobertura parcial** (quando um sinal esteve ausente e os pesos foram renormalizados), o timestamp e o ator. Além disso, o carimbo SHALL incluir a **explicação textual gerada** (quando houver classificação), a **versão do template** de explicação (`VersaoTemplate`) e o **motivo tipado de sem-classificação** (`MotivoSemClassificacao`, quando aplicável). Quando um atributo proibido for filtrado da entrada, a auditoria SHALL registrar o **evento de conformidade** correspondente. O `caseId` MUST correlacionar o registro de auditoria ao caso e à requisição de origem. O carimbo MUST NOT conter atributos sensíveis proibidos.

#### Scenario: Caso processado gera carimbo rastreável

- **WHEN** o Worker finaliza o processamento de um sinistro
- **THEN** existe um registro de auditoria com sinais+origem, score, faixa, versão da config, versão do provider, versão do template, explicação (quando classificado) ou motivo de sem-classificação, rota, indicação de cobertura parcial, timestamp e ator, todos correlacionados pelo `caseId`

#### Scenario: Falha do provider também é auditada

- **WHEN** o `IScoreProvider` falha e o caso nasce como `PENDENTE_REVISAO_MANUAL`
- **THEN** a auditoria registra a falha, a ausência de score e o `MotivoSemClassificacao` correspondente, mantendo o caso visível e rastreável

#### Scenario: Cobertura parcial é carimbada

- **WHEN** o caso é pontuado com apenas 2 dos 3 sinais (pesos renormalizados)
- **THEN** a auditoria registra a indicação de cobertura parcial junto do score, faixa e versão da config

#### Scenario: Atributo proibido filtrado gera evento de conformidade

- **WHEN** um atributo sensível proibido é filtrado da entrada de sinais antes do cálculo
- **THEN** a auditoria registra o evento de conformidade, sem incluir o atributo proibido no carimbo

#### Scenario: Classificação registra faixa, explicação e versões de forma imutável

- **WHEN** um caso é classificado e o processamento é concluído
- **THEN** o registro de auditoria contém a faixa, a explicação textual e as versões de limiares (`scoring_config`) e de template (`VersaoTemplate`) usadas, e esse registro é imutável (UPDATE/DELETE bloqueados)
