# claim-processing-worker — Delta Spec

## MODIFIED Requirements

### Requirement: Fail-open em falha ou sinal faltante

O Worker SHALL criar o caso no estado `PENDENTE_REVISAO_MANUAL`, registrar a
falha/ausência na trilha de auditoria e roteá-lo para revisão humana quando o
`IScoreProvider` lança exceção ou dá timeout, ou quando **nenhum** sinal pôde ser
calculado (todos indisponíveis). Com indisponibilidade **parcial** (ao menos um sinal calculado), o
caso SHALL seguir o fluxo normal de score com os sinais disponíveis, marcado como dados
incompletos. Em nenhum ramo o Worker rejeita, bloqueia ou descarta o sinistro; o caso
sempre nasce e fica visível.

#### Scenario: Provider indisponível não bloqueia o sinistro

- **WHEN** o `IScoreProvider` está indisponível (mock em modo "simular queda") ao processar um sinistro
- **THEN** o Worker cria o caso como `PENDENTE_REVISAO_MANUAL`, registra a falha na auditoria, e o sinistro segue — nada é bloqueado

#### Scenario: Indisponibilidade parcial segue para score com marca de dados incompletos

- **WHEN** um sinistro é processado com 1 ou 2 sinais indisponíveis e os demais calculados
- **THEN** o Worker calcula o score com os sinais disponíveis, o caso segue o fluxo
  normal de roteamento com `DadosIncompletos` marcado, e a auditoria registra o motivo de
  cada indisponibilidade — sem assumir valor baixo nem alto para o sinal ausente

## REMOVED Requirements

### Requirement: Consumo do payload de sinistro real sem sinais computados

**Reason**: A feature 2.2 (coleta de sinais) passa a existir — o Worker agora computa os
3 sinais fixos antes da decisão; a premissa "nenhum sinal acompanha o sinistro" deixa de
valer.
**Migration**: Substituído pelo requirement "Coleta de sinais antes da decisão" abaixo. O
fail-open para ausência total de sinais calculáveis e a preservação de `payloadParcial`
permanecem cobertos pelos novos cenários.

## ADDED Requirements

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
