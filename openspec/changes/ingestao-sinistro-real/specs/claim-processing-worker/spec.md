## ADDED Requirements

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
