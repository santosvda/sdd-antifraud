## Why

Hoje o `IScoreProvider` é um mock explícito e sinalizado — nenhum score real é calculado. A Feature 2.3 (Score & Regras) entrega o **motor de score determinístico** que combina os 3 sinais de fraude em um valor 0–100, classifica em faixa de risco e o faz de forma reprodutível, explicável e auditável, com pesos/limiares governados e sem que qualquer atributo sensível proibido influencie o cálculo. É o pré-requisito para o roteamento real (Feature 2.5) parar de depender de um score fabricado.

## What Changes

- **Motor de regras determinístico** atrás do `IScoreProvider`: score = soma dos pesos dos sinais **booleanos** verdadeiros (reuso de imagem, IMEI×série, velocity). Substitui a lógica do mock em caminho real (mock permanece só para testes).
- **Renormalização por cobertura parcial**: com exatamente 2 dos 3 sinais presentes, os pesos dos presentes são renormalizados para somar 100 e o caso é marcado `CoberturaParcial`. Com 0 ou 1 sinal (piso mínimo **≥2**), não há score — o caso vira "não avaliado" (fail-open), evitando que 1 sinal renormalizado dirija uma faixa de risco confiante (mitiga o mascaramento do PRD §18).
- **Filtro de atributos proibidos** explícito (raça/cor, gênero, orientação sexual, religião, deficiência, idade) antes do motor, reforçado por whitelist fechado dos 3 sinais válidos; sinal filtrado gera evento de conformidade auditado.
- **Classificação de faixa** por limiares da config ativa: baixo `<30`, médio `30–70`, alto `>70` (limiar_medio=30, limiar_alto=71).
- **BREAKING** — contrato da porta: `IScoreProvider.CalcularScoreAsync` deixa de devolver `Task<int>` e passa a devolver `Task<ResultadoScore>` (`Score?`, `CoberturaParcial`, `SinaisUsados`, `SinaisAusentes`, `MotivoNaoAvaliado`). A renormalização e o piso de cobertura moram no motor, não no orquestrador.
- **Novo campo `CoberturaParcial`** persistido em `Caso` e `RegistroAuditoria` (distinto de `PayloadParcial` e `DadosIncompletos`).
- **Config v2** publicada na `scoring_config` (pesos `reuso_imagem`=50 / `imei_serie`=30 / `velocity`=20; limiares 30/71) e ativada; v1 (placeholder do mock, valores divergentes do PRD) fica inativa — honra o versionamento (BR7).
- **Velocity chega pronto** como sinal booleano da Feature 2.2 (outro dev); a 2.3 **não** consulta histórico — permanece função pura `(sinais, config) → score` (determinismo RF10).

## Capabilities

### New Capabilities

- `risk-score-engine`: motor determinístico de cálculo de score atrás do `IScoreProvider` — soma booleana ponderada dos 3 sinais fixos, renormalização com piso de cobertura ≥2, marcação de cobertura parcial, piso → "não avaliado", filtro de atributos proibidos, classificação de faixa e determinismo/reprodutibilidade por versão de config.

### Modified Capabilities

- `claim-processing-worker`: o requisito "score via porta mock, nenhum valor fabricado em caminho real" e o cenário "sinal parcial → revisão manual" mudam. Passa a usar o provider real determinístico; a semântica de sinal ausente muda (0–1 sinal → não avaliado/fail-open; 2 de 3 → renormaliza, pontua e marca cobertura parcial); consome o novo `ResultadoScore` e propaga `CoberturaParcial`.
- `immutable-audit-trail`: o carimbo de decisão completo passa a incluir `CoberturaParcial` e o evento de conformidade quando um sinal é filtrado por corresponder a atributo proibido.

## Impact

- **Core** (`Antifraude.Core`): novo `ResultadoScore`; `IScoreProvider` com retorno alterado (**BREAKING**); novo `MotorDeRegras` (impl pura da porta) e `FiltroAtributosProibidos` em `Decisao`; ajuste no `MotorDeDecisao` (remove pré-check `SinaisIncompletos`, consome `ResultadoScore`, mapeia `Score null → FailOpen`, seta `CoberturaParcial`); campo `CoberturaParcial` em `Caso` e `RegistroAuditoria`; constantes de nome dos 3 sinais.
- **Infra** (`Antifraude.Infra`): migration adicionando colunas `cobertura_parcial` em `casos` e `auditoria` + seed da `scoring_config` v2 e desativação da v1; mapeamento EF das colunas novas; DI troca o mock pelo `MotorDeRegras` como `IScoreProvider` (mock atualizado ao novo retorno, mantido para testes).
- **scoring-config-store**: publica v2 (dado sob o requisito existente de config versionada — sem mudança de requisito).
- **Testes** (`Antifraude.Tests`): unit do motor (soma booleana, renormalização 2 de 3, piso <2 → `Score null`, filtro + auditoria de conformidade, determinismo/arredondamento), bordas do `Classificador` (29/30/70/71), filtro; integração ponta-a-ponta do worker com a config v2.
- **Sem impacto de escopo**: roteamento (2.5), coleta real de sinais (2.2), UI de painel — permanecem fora.
