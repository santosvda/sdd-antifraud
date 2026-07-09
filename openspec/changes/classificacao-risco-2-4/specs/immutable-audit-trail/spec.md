## MODIFIED Requirements

### Requirement: Carimbo de decisão completo por caso

Cada caso processado SHALL registrar na auditoria: os sinais recebidos e sua origem, o score, a faixa, a **versão da `scoring_config`** vigente, a **versão do `IScoreProvider`**, a rota atribuída, o timestamp e o ator. Além disso, o carimbo SHALL incluir a **explicação textual gerada** (quando houver classificação), a **versão do template** de explicação (`VersaoTemplate`) e o **motivo tipado de sem-classificação** (`MotivoSemClassificacao`, quando aplicável). O `caseId` MUST correlacionar o registro de auditoria ao caso e à requisição de origem.

#### Scenario: Caso processado gera carimbo rastreável

- **WHEN** o Worker finaliza o processamento de um sinistro
- **THEN** existe um registro de auditoria com sinais+origem, score, faixa, versão da config, versão do provider, versão do template, explicação (quando classificado) ou motivo de sem-classificação, rota, timestamp e ator, todos correlacionados pelo `caseId`

#### Scenario: Falha do provider também é auditada

- **WHEN** o `IScoreProvider` falha e o caso nasce como `PENDENTE_REVISAO_MANUAL`
- **THEN** a auditoria registra a falha, a ausência de score e o `MotivoSemClassificacao` correspondente, mantendo o caso visível e rastreável

#### Scenario: Classificação registra faixa, explicação e versões de forma imutável

- **WHEN** um caso é classificado e o processamento é concluído
- **THEN** o registro de auditoria contém a faixa, a explicação textual e as versões de limiares (`scoring_config`) e de template (`VersaoTemplate`) usadas, e esse registro é imutável (UPDATE/DELETE bloqueados)
