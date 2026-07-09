## ADDED Requirements

### Requirement: Classificação do score em faixa por limiar fechado-aberto

O sistema SHALL classificar um score numérico em uma das três faixas usando os limiares da `scoring_config` vigente e **intervalo fechado-aberto**: baixo `[0, LimiarMedio)`, médio `[LimiarMedio, LimiarAlto)`, alto `[LimiarAlto, 100]`. Os limiares MUST vir da configuração versionada, nunca hard-coded.

#### Scenario: Score no interior de uma faixa

- **WHEN** um score de 72 é classificado com limiares médio=30 e alto=70
- **THEN** a faixa atribuída é "alto"

#### Scenario: Score no limite exato pertence à faixa superior

- **WHEN** um score de exatamente 30 é classificado com limiar médio=30
- **THEN** a faixa atribuída é "médio", não "baixo"

### Requirement: Explicação textual determinística e não-acusatória

O sistema SHALL gerar uma explicação textual associada à faixa por meio de um **template determinístico definido em código e versionado** (`VersaoTemplate`), sem geração livre de linguagem natural. A explicação MUST mencionar o valor do score e a faixa, e MUST NOT afirmar fraude como fato consumado — sempre linguagem de indício/hipótese. Mesma entrada + mesma versão de template produzem sempre a mesma frase.

#### Scenario: Explicação menciona score e faixa em linguagem de indício

- **WHEN** a explicação é gerada para um score de 72 na faixa "alto"
- **THEN** o texto menciona o score e a faixa e usa linguagem de indício, sem afirmar fraude como fato consumado

#### Scenario: Explicação é determinística

- **WHEN** a explicação é gerada duas vezes para o mesmo score e a mesma versão de template
- **THEN** o texto resultante é idêntico nas duas execuções

### Requirement: Nomeação dos sinais ativados por nome de exibição

A explicação SHALL nomear os **sinais ativados** — definidos como sinais presentes com `Valor > 0` — usando um nome de exibição obtido de um mapa em código versionado junto do template. Um sinal cujo identificador não conste do mapa MUST receber um nome de fallback seguro; o identificador técnico cru MUST NOT vazar para o texto.

#### Scenario: Sinais ativados são nomeados

- **WHEN** a explicação é gerada com os sinais de reuso de imagem e IMEI×série ativados (`Valor > 0`)
- **THEN** o texto nomeia esses sinais por seu nome de exibição, em linguagem de indício

#### Scenario: Sinal desconhecido usa fallback

- **WHEN** a explicação é gerada com um sinal cujo identificador não está no mapa de nomes de exibição
- **THEN** o texto usa um nome de fallback seguro e não expõe o identificador técnico cru

### Requirement: Menção explícita de cobertura parcial

Quando o score tiver sido calculado com cobertura parcial (pesos renormalizados por ausência de um ou mais sinais), a explicação SHALL mencionar explicitamente essa condição e nomear apenas os sinais efetivamente avaliados. A informação de cobertura parcial MUST ser recebida como entrada própria da classificação, sem alterar o contrato do provedor de score.

#### Scenario: Cobertura parcial é mencionada

- **WHEN** a explicação é gerada para um score marcado como cobertura parcial
- **THEN** o texto menciona explicitamente que o cálculo teve cobertura parcial

#### Scenario: Cobertura total não menciona parcialidade

- **WHEN** a explicação é gerada para um score sem cobertura parcial
- **THEN** o texto não menciona cobertura parcial

### Requirement: Fail-open upstream não gera classificação inventada

Quando o score não foi calculado por indisponibilidade esperada (fail-open upstream), o sistema MUST NOT atribuir faixa nem gerar explicação de faixa. Em vez disso, SHALL produzir a marca de "sem classificação" — um `MotivoSemClassificacao` de indisponibilidade esperada acompanhado de um **rótulo canônico** curto e não-acusatório derivado do motivo.

#### Scenario: Caso não avaliado não recebe faixa inventada

- **WHEN** a classificação processa um caso sinalizado como "não avaliado" por indisponibilidade
- **THEN** nenhuma faixa é atribuída, nenhuma explicação de faixa é gerada, e a marca de "sem classificação" com rótulo canônico é produzida

### Requirement: Score fora do intervalo é anomalia técnica

Um score fora do intervalo `[0, 100]` (ex.: negativo ou maior que 100, por erro upstream) SHALL ser tratado como equivalente ao fail-open para fins de roteamento (sem classificação, revisão manual), com `MotivoSemClassificacao` de anomalia (`ScoreForaDeFaixa`). O sistema MUST NOT coagir o valor para dentro do intervalo (sem clamp silencioso).

#### Scenario: Score negativo não é classificado nem coagido

- **WHEN** a classificação recebe um score de -5
- **THEN** nenhuma faixa é atribuída, o caso é marcado como sem classificação com motivo `ScoreForaDeFaixa`, e o valor não é coagido para 0

### Requirement: Motivo tipado distingue indisponibilidade de anomalia

Cada caso sem classificação SHALL carimbar um `MotivoSemClassificacao` tipado que distinga indisponibilidade esperada (ex.: `SinalAusente`, `ProviderIndisponivel`) de anomalia técnica (ex.: `ScoreForaDeFaixa`, `ConfigIndisponivel`, `ConfigCorrompida`). A decisão de emitir alerta técnico MUST derivar do motivo, não de texto livre. Problemas de configuração de limiares (indisponível ou corrompida) são anomalias — nunca se opera sem limiares validados.

#### Scenario: Indisponibilidade esperada e anomalia recebem motivos distintos

- **WHEN** um caso não é classificado por provider indisponível e outro por score fora de faixa
- **THEN** o primeiro recebe um motivo de indisponibilidade esperada e o segundo um motivo de anomalia, distinguíveis para fins de auditoria e alerta
