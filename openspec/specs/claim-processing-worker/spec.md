# claim-processing-worker Specification

## Purpose

Processamento assíncrono do sinistro: o `Worker` consome mensagens do SQS, obtém o score via `IScoreProvider`, persiste o caso no MySQL com a auditoria correspondente e roteia todo caso para revisão humana. Nenhuma ação final é automática e o fluxo é fail-open.

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

O Worker SHALL obter o score exclusivamente através da interface `IScoreProvider`. Na fundação a implementação é um mock explícito e **sinalizado como mock**; nenhum valor de score é fabricado em caminho real fora dessa sinalização.

#### Scenario: Provider mock é sinalizado

- **WHEN** o Worker chama o `IScoreProvider` placeholder e persiste o caso
- **THEN** a auditoria do caso registra que o score veio de um provider mock (versão/sinalização do provider carimbada)

### Requirement: Fail-open em falha ou sinal faltante

Quando um sinal está faltando/parcial, ou quando o `IScoreProvider` lança exceção ou dá timeout, o Worker SHALL criar o caso no estado `PENDENTE_REVISAO_MANUAL`, registrar a falha/ausência na trilha de auditoria, e roteá-lo para revisão humana. Em nenhum ramo o Worker rejeita, bloqueia ou descarta o sinistro; o caso sempre nasce e fica visível.

#### Scenario: Provider indisponível não bloqueia o sinistro

- **WHEN** o `IScoreProvider` está indisponível (mock em modo "simular queda") ao processar um sinistro
- **THEN** o Worker cria o caso como `PENDENTE_REVISAO_MANUAL`, registra a falha na auditoria, e o sinistro segue — nada é bloqueado

#### Scenario: Sinal parcial roteia para revisão manual

- **WHEN** um sinistro chega com sinais faltantes ou parciais
- **THEN** o Worker calcula com o que tem, marca o caso como dados incompletos, roteia para revisão manual e registra a ausência na auditoria — sem assumir score baixo nem alto por omissão

### Requirement: Consumo do payload de sinistro real sem sinais computados

O Worker SHALL desserializar o payload de sinistro real (`idSinistro`, apólice, aparelho,
fotos por referência, metadados) e a marca `payloadParcial` herdada da ingestão. Como a
feature de coleta de sinais ainda não existe, nenhum sinal de fraude acompanha o sinistro; o
Worker MUST tratar a ausência de sinais computados via fail-open, criando o caso como
`PENDENTE_REVISAO_MANUAL` e roteando para revisão humana, sem fabricar score. A marca
`payloadParcial` MUST ser preservada na auditoria do caso.

#### Scenario: Sinistro real sem sinais roteia para revisão manual

- **WHEN** o Worker consome um sinistro real sem sinais computados
- **THEN** cria o caso como `PENDENTE_REVISAO_MANUAL`, registra na auditoria a ausência de
  sinais e roteia para revisão humana — sem negar, aprovar, bloquear ou fabricar score

#### Scenario: Marca de payload parcial é preservada

- **WHEN** o Worker consome um sinistro marcado como `payloadParcial`
- **THEN** o caso persistido e sua auditoria registram a condição de payload parcial
