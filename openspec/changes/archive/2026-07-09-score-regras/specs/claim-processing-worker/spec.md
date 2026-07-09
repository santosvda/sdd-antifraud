## MODIFIED Requirements

### Requirement: Score obtido através de porta abstrata

O Worker SHALL obter o score exclusivamente através da interface `IScoreProvider`, cujo retorno é um resultado estruturado (`ResultadoScore`: score opcional, cobertura parcial, sinais usados, sinais ausentes, motivo de "não avaliado"). No caminho real a implementação é o **motor de regras determinístico** (`risk-score-engine`), com sua versão carimbada na auditoria; o mock explícito e **sinalizado como mock** permanece disponível para testes. Nenhum valor de score MUST ser fabricado fora dessa porta, e a versão/sinalização do provider MUST ser carimbada em todo caso.

#### Scenario: Score real vem do motor determinístico com versão carimbada

- **WHEN** o Worker processa um sinistro com sinais suficientes no caminho real
- **THEN** o score vem do motor de regras determinístico e a auditoria carimba a versão do provider e a versão da config usada

#### Scenario: Provider mock é sinalizado

- **WHEN** o Worker chama o `IScoreProvider` mock e persiste o caso
- **THEN** a auditoria do caso registra que o score veio de um provider mock (versão/sinalização do provider carimbada)

### Requirement: Fail-open em falha ou sinal faltante

Quando o `IScoreProvider` devolve "não avaliado" (cobertura de sinais abaixo do piso de 2), quando não há configuração de scoring ativa, ou quando o provider lança exceção ou dá timeout, o Worker SHALL criar o caso no estado `PENDENTE_REVISAO_MANUAL`, registrar a causa na trilha de auditoria e roteá-lo para revisão humana, sem fabricar score. Quando o provider devolve um score com **cobertura parcial** (exatamente 2 dos 3 sinais, pesos renormalizados), o Worker SHALL persistir o score e a faixa, marcar o caso e a auditoria como **cobertura parcial**, e roteá-lo pela faixa classificada. Em nenhum ramo o Worker rejeita, bloqueia ou descarta o sinistro; o caso sempre nasce e fica visível.

#### Scenario: Provider indisponível não bloqueia o sinistro

- **WHEN** o `IScoreProvider` está indisponível (exceção/timeout) ao processar um sinistro
- **THEN** o Worker cria o caso como `PENDENTE_REVISAO_MANUAL`, registra a falha na auditoria, e o sinistro segue — nada é bloqueado

#### Scenario: Cobertura abaixo do piso não é avaliada

- **WHEN** um sinistro chega com 0 ou 1 dos 3 sinais presentes
- **THEN** o provider devolve "não avaliado", o Worker cria o caso como `PENDENTE_REVISAO_MANUAL`, registra a ausência na auditoria e roteia para revisão humana — sem assumir score baixo nem alto por omissão

#### Scenario: Cobertura parcial pontua e é marcada

- **WHEN** um sinistro chega com exatamente 2 dos 3 sinais presentes
- **THEN** o Worker persiste o score renormalizado e a faixa, marca o caso e a auditoria como cobertura parcial, e roteia pela faixa classificada — sem bloquear
