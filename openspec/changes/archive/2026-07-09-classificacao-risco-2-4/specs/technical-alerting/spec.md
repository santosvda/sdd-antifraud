## ADDED Requirements

### Requirement: Canal de alerta técnico distinto do operacional

O sistema SHALL expor uma porta de alerta técnico (`IAlertaTecnico`) no domínio, distinta de qualquer canal de alerta operacional. A porta MUST aceitar severidade, um código de anomalia e contexto correlacionável (incluindo o `caseId`). Na fundação, o adapter SHALL emitir um log estruturado de nível Critical; a porta MUST permitir plugar um canal real (ex.: plantão/PagerDuty) sem alterar o domínio.

#### Scenario: Alerta técnico é emitido com correlação por caseId

- **WHEN** o domínio emite um alerta técnico para um caso
- **THEN** o adapter registra um evento estruturado de nível Critical contendo o código da anomalia e o `caseId`

### Requirement: Anomalia técnica dispara alerta de severidade alta

Uma anomalia do motor de classificação — score fora do intervalo `[0,100]` (`ScoreForaDeFaixa`) ou configuração de limiares indisponível/corrompida (`ConfigIndisponivel`, `ConfigCorrompida`) — SHALL disparar um alerta técnico de **severidade alta**. Casos de indisponibilidade esperada (fail-open padrão: sinal ausente, provider indisponível) MUST NOT disparar esse alerta, para não confundir bug com indisponibilidade prevista.

#### Scenario: Score fora de faixa dispara alerta severidade alta

- **WHEN** um caso é marcado como sem classificação por score fora do intervalo `[0,100]`
- **THEN** um alerta técnico de severidade alta é emitido

#### Scenario: Provider indisponível não dispara alerta técnico

- **WHEN** um caso cai em fail-open por provider indisponível (indisponibilidade esperada)
- **THEN** nenhum alerta técnico de severidade alta é emitido — apenas o registro de auditoria do fail-open
