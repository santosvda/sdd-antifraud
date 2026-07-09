## Why

Hoje o pipeline entrega um score numérico e uma faixa, mas nenhuma **explicação legível** ao analista, e a lógica de classificação tem lacunas que ferem guardrails: `MotorDeDecisao` faz `Math.Clamp(score, 0, 100)`, mascarando silenciosamente um score anômalo (ex.: -5 ou 150) em vez de sinalizá-lo, e todos os casos "sem classificação" colapsam em `Faixa.Indeterminado` com uma `Causa` de texto livre — impossível distinguir indisponibilidade esperada (fail-open) de anomalia técnica (bug upstream). A Feature 2.4 formaliza a **Classificação de Risco** como contrato próprio: traduz o score em faixa + explicação não-acusatória e torna os casos sem classificação tipados e auditáveis.

## What Changes

- Novo **gerador de explicação textual determinístico** (template em código, versionado por `VersaoTemplate`), que nomeia os sinais ativados (`Valor > 0`) por nome de exibição, em linguagem de indício, e menciona cobertura parcial quando aplicável.
- Novo **motivo tipado** (`MotivoSemClassificacao`) que distingue indisponibilidade esperada (`SinalAusente`, `ProviderIndisponivel`) de anomalia técnica (`ConfigIndisponivel`, `ConfigCorrompida`, `ScoreForaDeFaixa` — problemas de config alertam: nunca operar sem limiares validados), carimbado no caso e na auditoria ao lado da `Causa` textual.
- **BREAKING:** remoção do `Math.Clamp(score, 0, 100)` no `MotorDeDecisao`. Score fora de [0,100] passa a ser tratado como anomalia (sem classificação, revisão manual) + alerta técnico de severidade alta — em vez de ser coagido silenciosamente para a faixa.
- Novo **canal de alerta técnico** (`IAlertaTecnico`), distinto do alerta operacional, disparado por motivo de anomalia (score fora de faixa, config corrompida).
- **Explicação e versões persistidas** no caso e na trilha de auditoria (`Explicacao`, `VersaoTemplate`, `MotivoSemClassificacao`) — migration aditiva.
- Em fail-open upstream (RF06), **nenhuma faixa ou explicação de faixa é inventada**: a "marca" é o motivo tipado + um rótulo canônico não-acusatório derivado do enum.

**Restrição de fronteira:** as features 2.2 e 2.3 estão sendo feitas em paralelo por outros devs. Esta change é **aditiva** — não altera o contrato de score (`IScoreProvider`, `Sinal`, `Sinistro`, `ScoringConfig`); toca o `MotorDeDecisao` apenas num seam mínimo, a combinar no merge.

## Capabilities

### New Capabilities
- `risk-classification`: Tradução do score em faixa de risco + explicação textual determinística e não-acusatória; definição de "sinal ativado"; distinção tipada de casos sem classificação (fail-open esperado vs. anomalia); versionamento do template; determinismo e auditabilidade da classificação.
- `technical-alerting`: Canal de alerta técnico de severidade alta (`IAlertaTecnico`), distinto do canal operacional, para anomalias do motor (score fora de faixa, configuração corrompida). Adapter na fundação emite log estruturado nível Critical correlacionado por `caseId`; gancho pronto para plantão/PagerDuty.

### Modified Capabilities
- `claim-processing-worker`: o pipeline de decisão (`MotorDeDecisao`) deixa de coagir o score via clamp; passa a detectar score fora de [0,100] como anomalia (sem classificação + alerta), a invocar o gerador de explicação, e a carimbar o motivo tipado em cada caso sem classificação.
- `immutable-audit-trail`: o carimbo de decisão por caso passa a incluir a **explicação textual gerada**, a **versão do template** (`VersaoTemplate`) e o **motivo tipado de sem-classificação** (`MotivoSemClassificacao`).

## Impact

- **Core (`Antifraude.Core`):** novos `GeradorDeExplicacao`, enum `MotivoSemClassificacao`, porta `IAlertaTecnico`, módulo de template (texto + mapa de nomes de exibição + rótulos canônicos); extensão do `Classificador` (detecção out-of-range); campos novos em `Caso`, `RegistroAuditoria` e `ResultadoDecisao`; seam mínimo no `MotorDeDecisao` (remove clamp, delega anomalia, invoca gerador).
- **Infra (`Antifraude.Infra`):** adapter de `IAlertaTecnico` (log estruturado Critical); migration EF aditiva (colunas `Explicacao`, `VersaoTemplate`, `MotivoSemClassificacao` em `casos` e `auditoria` — DDL aditivo compatível com o trigger de imutabilidade); registro de DI.
- **Tests (`Antifraude.Tests`):** unit do gerador e do classificador com entradas sintéticas; integração com um `IScoreProvider` de teste (no projeto de testes, sem tocar o `MockScoreProvider` compartilhado) que injeta sinais e scores fora de faixa.
- **Sem impacto** no contrato de score (2.3) nem na coleta de sinais (2.2). No pipeline vivo atual todo caso ainda cai em fail-open (sinais não fluem até a 2.2); os caminhos novos são exercitáveis por unit + test-double.
