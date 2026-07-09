# claim-processing-worker Specification

## Purpose

Processamento assíncrono do sinistro: o `Worker` consome mensagens do SQS, coleta os 3 sinais de risco (feature 2.2), obtém o score via `IScoreProvider`, persiste o caso no MySQL com a auditoria correspondente e roteia todo caso para revisão humana. Nenhuma ação final é automática e o fluxo é fail-open.

## Requirements

### Requirement: Consumo assíncrono da fila

O `Worker` SHALL rodar como `BackgroundService`, consumir mensagens do SQS, e processar cada sinistro de forma independente da API. API e Worker MUST NOT se chamar diretamente — a comunicação ocorre pela fila (entrada) e pelo MySQL (estado).

#### Scenario: Mensagem enfileirada é processada

- **WHEN** uma mensagem de sinistro chega ao SQS
- **THEN** o Worker a consome, produz um caso persistido no MySQL e registra a auditoria correspondente, correlacionados pelo mesmo `caseId`

### Requirement: Todo caso é roteado para revisão humana

O Worker SHALL sempre produzir um caso roteado para uma fila humana (normal ou reforçada). Nenhuma ação final sobre o sinistro é automática; o sistema MUST NOT negar, aprovar ou bloquear o sinistro em nenhum ramo.

#### Scenario: Caso de risco alto vai para fila reforçada sem bloquear

- **WHEN** o `IScoreProvider` indica risco alto para um sinistro
- **THEN** o caso é roteado para a fila reforçada e o sinistro segue seu curso — não há estado que negue, aprove ou bloqueie

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

### Requirement: Coleta de sinais antes da decisão

O Worker SHALL, após desserializar o payload de sinistro real, executar a coleta dos 3
sinais fixos (`reuso_imagem`, `imei_serie_divergente`, `velocity`) e invocar o motor de
decisão com o sinistro enriquecido pelo conjunto de sinais (calculados e/ou
indisponíveis). A marca `payloadParcial` herdada da ingestão MUST ser preservada no caso
e na auditoria. A coleta MUST NOT rejeitar, bloquear ou descartar o sinistro em nenhum
ramo.

#### Scenario: Sinistro processado carrega os 3 sinais

- **WHEN** o Worker consome um sinistro real com payload completo e fontes disponíveis
- **THEN** o caso é decidido a partir dos 3 sinais coletados e a auditoria registra o
  estado e a evidência de cada um

#### Scenario: Marca de payload parcial é preservada

- **WHEN** o Worker consome um sinistro marcado como `payloadParcial`
- **THEN** o caso persistido e sua auditoria registram a condição de payload parcial,
  e os sinais cujo dado de entrada falta são marcados como "indisponível"

### Requirement: Ausência total de sinais calculáveis segue fail-open

Quando os 3 sinais resultam "indisponível" (nenhum pôde ser calculado), o Worker SHALL
tratar o caso como não avaliado: criar o caso como `PENDENTE_REVISAO_MANUAL`, marcar
dados incompletos, registrar os motivos por sinal na auditoria e rotear para revisão
humana — sem fabricar score.

#### Scenario: Todos os sinais indisponíveis roteia para revisão manual

- **WHEN** as 3 fontes de dados estão indisponíveis ao processar um sinistro
- **THEN** o caso nasce como `PENDENTE_REVISAO_MANUAL` com os 3 sinais marcados como
  "indisponível" na auditoria, cada um com seu motivo, e o sinistro segue sem bloqueio
