## ADDED Requirements

### Requirement: Score determinístico por soma booleana ponderada

O motor SHALL calcular o score como a **soma dos pesos** dos sinais **booleanos verdadeiros**, usando os pesos da versão ativa da `scoring_config`. Os sinais são tratados como booleanos (ativo/inativo); o motor MUST NOT usar valor graduado. O cálculo SHALL ser determinístico e reprodutível: a mesma entrada de sinais com a mesma versão de configuração MUST produzir sempre o mesmo score e a mesma faixa (RF10). Quando a renormalização produzir peso fracionário, o score final SHALL ser arredondado por uma convenção única e fixa (metade para cima) e clampado ao intervalo [0,100].

#### Scenario: Três sinais verdadeiros somam seus pesos

- **WHEN** os 3 sinais (`reuso_imagem`, `imei_serie`, `velocity`) chegam presentes e verdadeiros com a config v2 (50/30/20)
- **THEN** o score é 100 e nenhuma cobertura parcial é marcada

#### Scenario: Sinal presente e falso soma zero

- **WHEN** `reuso_imagem` e `imei_serie` chegam verdadeiros e `velocity` chega presente e falso, com a config v2
- **THEN** o score é 80 e o caso não é marcado como cobertura parcial (os 3 sinais estão presentes)

#### Scenario: Resultado é idêntico entre execuções

- **WHEN** o motor calcula duas vezes o score do mesmo conjunto de sinais com a mesma versão de config
- **THEN** o score e a faixa são idênticos nas duas execuções

### Requirement: Conjunto fechado de sinais com estado explícito

O motor SHALL operar sobre um conjunto **fechado** de 3 sinais esperados (`reuso_imagem`, `imei_serie`, `velocity`), mapeando a entrada para um estado explícito por sinal: presente-verdadeiro, presente-falso ou ausente. Um sinal SHALL ser considerado **ausente** quando seu nome esperado não vier na entrada; MUST NOT inferir ausência apenas pela contagem de itens. Um nome de sinal fora do conjunto esperado MUST NOT influenciar o score.

#### Scenario: Sinal esperado que não veio é tratado como ausente

- **WHEN** a entrada traz `reuso_imagem` e `imei_serie` mas não traz `velocity`
- **THEN** o motor marca `velocity` como ausente e o considera na decisão de cobertura, sem assumir verdadeiro nem falso

#### Scenario: Nome de sinal desconhecido não entra no cálculo

- **WHEN** a entrada traz um sinal com nome fora do conjunto esperado
- **THEN** esse sinal é descartado e o score não reflete seu valor

### Requirement: Renormalização com piso de cobertura

Quando exatamente 2 dos 3 sinais estiverem presentes, o motor SHALL **renormalizar** proporcionalmente os pesos dos sinais presentes para somar 100, calcular o score sobre eles e marcar o resultado como **cobertura parcial**. Quando 0 ou 1 sinal estiver presente (abaixo do piso mínimo de 2), o motor MUST NOT calcular nem estimar score — SHALL devolver "não avaliado" com o motivo registrado. A renormalização MUST NOT ser aplicada com apenas 1 sinal presente.

#### Scenario: Dois sinais presentes renormalizam e marcam cobertura parcial

- **WHEN** apenas `reuso_imagem` (peso 50) e `imei_serie` (peso 30) estão presentes, ambos verdadeiros, com a config v2
- **THEN** os pesos são renormalizados para somar 100 (62,5 e 37,5), o score é 100 e o resultado é marcado como cobertura parcial

#### Scenario: Um único sinal presente não é avaliado

- **WHEN** apenas 1 dos 3 sinais está presente
- **THEN** o motor devolve "não avaliado" (sem score) com o motivo de cobertura abaixo do piso

#### Scenario: Nenhum sinal presente não é avaliado

- **WHEN** nenhum dos 3 sinais esperados está presente
- **THEN** o motor devolve "não avaliado" (sem score)

### Requirement: Filtro de atributos proibidos

O motor SHALL filtrar, **antes do cálculo**, qualquer entrada correspondente a atributo sensível proibido (raça/cor, gênero, orientação sexual, religião, deficiência, idade), de modo que o score nunca reflita esses atributos. O conjunto fechado dos 3 sinais válidos SHALL funcionar como whitelist (nega por padrão). Quando um atributo proibido for detectado na entrada, o motor SHALL registrar um evento de conformidade auditável.

#### Scenario: Atributo proibido é filtrado antes do cálculo

- **WHEN** a entrada de sinais contém um atributo proibido
- **THEN** esse atributo é filtrado antes do cálculo, o score não o reflete e um evento de conformidade é registrado para auditoria

### Requirement: Classificação de faixa por limiar versionado

O motor SHALL classificar o score em uma faixa usando os limiares da versão ativa da `scoring_config`: baixo quando `score < limiar_medio`, médio quando `limiar_medio <= score < limiar_alto`, alto quando `score >= limiar_alto`. Os limiares MUST NOT ser hard-coded. Os valores padrão da v2 SHALL ser `limiar_medio = 30` e `limiar_alto = 71` (materializando "baixo <30 / médio 30–70 / alto >70").

#### Scenario: Score na borda inferior do médio

- **WHEN** o score é 30 com a config v2
- **THEN** a faixa classificada é médio

#### Scenario: Score 70 é médio e 71 é alto

- **WHEN** o score é 70 com a config v2
- **THEN** a faixa é médio; e quando o score é 71, a faixa é alto

#### Scenario: Score abaixo de 30 é baixo

- **WHEN** o score é 29 com a config v2
- **THEN** a faixa classificada é baixo

### Requirement: Velocity consumido como sinal pronto

O motor SHALL consumir `velocity` como um **sinal booleano já produzido** pela coleta de sinais (feature 2.2), representando "≥2 sinistros do mesmo cliente OU mesmo aparelho (IMEI) em janela de 90 dias". O motor MUST NOT consultar histórico de sinistros para computar velocity, permanecendo uma função pura de `(sinais, config)`.

#### Scenario: Velocity chega pronto e soma seu peso

- **WHEN** a entrada traz `velocity` verdadeiro
- **THEN** o motor soma o peso de velocity ao score sem consultar nenhuma fonte de histórico

### Requirement: Resultado de score estruturado e sem fabricação

O motor SHALL devolver um resultado estruturado contendo: o score (ou ausência de score quando "não avaliado"), a indicação de cobertura parcial, os sinais usados, os sinais ausentes e o motivo de "não avaliado" quando aplicável. O motor MUST NOT devolver um score parcial ou estimado quando não puder avaliar; a condição de "não avaliado" SHALL ser explícita para o consumidor tratar via fail-open.

#### Scenario: Resultado carrega cobertura e sinais usados

- **WHEN** o motor calcula sobre 2 sinais presentes
- **THEN** o resultado indica cobertura parcial, lista os sinais usados e os ausentes

#### Scenario: Não avaliado é explícito, sem score fabricado

- **WHEN** o motor não pode avaliar (cobertura abaixo do piso)
- **THEN** o resultado não traz score algum e sinaliza explicitamente "não avaliado" com o motivo
