## MODIFIED Requirements

### Requirement: Fail-open em falha ou sinal faltante

Quando um sinal está faltando/parcial, ou quando o `IScoreProvider` lança exceção ou dá timeout, o Worker SHALL criar o caso no estado `PENDENTE_REVISAO_MANUAL`, registrar a falha/ausência na trilha de auditoria, e roteá-lo para revisão humana. Em nenhum ramo o Worker rejeita, bloqueia ou descarta o sinistro; o caso sempre nasce e fica visível. Cada caso sem classificação MUST carimbar um `MotivoSemClassificacao` tipado (ex.: `SinalAusente`, `ProviderIndisponivel`, `ConfigIndisponivel`) ao lado da causa textual, distinguindo indisponibilidade esperada de anomalia técnica.

#### Scenario: Provider indisponível não bloqueia o sinistro

- **WHEN** o `IScoreProvider` está indisponível (mock em modo "simular queda") ao processar um sinistro
- **THEN** o Worker cria o caso como `PENDENTE_REVISAO_MANUAL`, registra a falha na auditoria com motivo `ProviderIndisponivel`, e o sinistro segue — nada é bloqueado

#### Scenario: Sinal parcial roteia para revisão manual

- **WHEN** um sinistro chega com sinais faltantes ou parciais
- **THEN** o Worker calcula com o que tem, marca o caso como dados incompletos com motivo `SinalAusente`, roteia para revisão manual e registra a ausência na auditoria — sem assumir score baixo nem alto por omissão

## ADDED Requirements

### Requirement: Score fora do intervalo não é coagido pelo pipeline

O pipeline de decisão MUST NOT coagir o score para dentro do intervalo `[0,100]` (sem `Math.Clamp` silencioso). Quando o score recebido está fora de `[0,100]`, o Worker SHALL tratar o caso como sem classificação (estado `PENDENTE_REVISAO_MANUAL`, revisão manual) com motivo `ScoreForaDeFaixa` e SHALL emitir um alerta técnico de severidade alta. O sinistro MUST seguir seu curso — nada é bloqueado.

#### Scenario: Score fora de faixa vira anomalia com alerta

- **WHEN** o pipeline recebe um score de -5 (fora de `[0,100]`) para um sinistro
- **THEN** o caso nasce sem classificação com motivo `ScoreForaDeFaixa`, um alerta técnico de severidade alta é emitido, e o valor não é coagido para dentro do intervalo

### Requirement: Explicação e motivo persistidos no caso

Para todo caso processado, o Worker SHALL persistir a explicação textual gerada (quando houver classificação) e, quando não houver, o `MotivoSemClassificacao` com seu rótulo canônico. A explicação e o motivo MUST ser persistidos no caso, correlacionados pelo `caseId`, de modo que o analista leia faixa e explicação já prontas — nunca um número solto sem contexto.

#### Scenario: Caso classificado persiste a explicação

- **WHEN** o Worker classifica um sinistro em uma faixa
- **THEN** o caso persistido contém a explicação textual gerada, legível pelo analista

#### Scenario: Caso sem classificação persiste o motivo e o rótulo

- **WHEN** o Worker processa um caso sem classificação (fail-open ou anomalia)
- **THEN** o caso persistido contém o `MotivoSemClassificacao` e seu rótulo canônico, sem faixa nem explicação de faixa inventada
