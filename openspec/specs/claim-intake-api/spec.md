# claim-intake-api Specification

## Purpose

Recepção de sinistros pela borda HTTP: o endpoint `POST /sinistros` valida o formato, enfileira no SQS e responde `202 Accepted` com um `caseId` de correlação, sem decidir o mérito do sinistro. Expõe também `GET /health` e a documentação OpenAPI em `/swagger`.

## Requirements

### Requirement: Recepção assíncrona do sinistro

O endpoint `POST /sinistros` SHALL receber o **payload de sinistro real** — `idSinistro`
(único campo estrutural), `apolice`, `aparelho` (IMEI/número de série), `fotos` (por
referência: ID/URL, nunca cópia) e `metadados` (data/hora de abertura, canal, idCliente) —,
aplicar a checagem de idempotência, enfileirar o sinistro no SQS e responder `202 Accepted`
sem esperar o processamento. A resposta MUST conter um identificador de correlação
(`caseId`) que acompanha o sinistro por todo o fluxo. A API MUST NOT exigir os sinais de
fraude no corpo — os sinais são responsabilidade das features de coleta downstream.

#### Scenario: Sinistro válido é aceito

- **WHEN** um cliente envia `POST /sinistros` com um payload contendo ao menos o `idSinistro`
- **THEN** a API enfileira a mensagem no SQS e responde `202` com um `caseId`

#### Scenario: Corpo ilegível é rejeitado na borda

- **WHEN** um cliente envia `POST /sinistros` com um corpo que não pode ser interpretado
  (ex.: JSON inválido)
- **THEN** a API responde `400` e NÃO enfileira nada

#### Scenario: Payload bem-formado sem idSinistro não retorna erro ao produtor

- **WHEN** um cliente envia `POST /sinistros` com JSON válido mas sem `idSinistro`
- **THEN** a API responde `202` e o evento é roteado para a fila de erro técnico (nunca um
  `400`), pois o sinistro já existe no sistema principal

### Requirement: Endpoint de saúde

A API SHALL expor `GET /health` que retorna `200` quando o processo está apto a atender.

#### Scenario: Liveness da API

- **WHEN** o healthcheck do container chama `GET /health`
- **THEN** a API responde `200`

### Requirement: Documentação OpenAPI navegável

A API SHALL expor Swagger/OpenAPI em `/swagger`, servindo como a "UI" da fatia API-only.

#### Scenario: Swagger disponível

- **WHEN** um desenvolvedor abre `http://localhost:8080/swagger`
- **THEN** vê a documentação interativa incluindo `POST /sinistros` e `GET /health`

### Requirement: A API nunca decide o mérito do sinistro

O endpoint de recepção SHALL apenas validar formato e enfileirar; ele MUST NOT negar, aprovar ou bloquear o sinistro, nem computar score.

#### Scenario: Recepção não emite veredito

- **WHEN** qualquer sinistro é recebido, independentemente do conteúdo dos sinais
- **THEN** a resposta é sempre `202` (aceito para processamento) ou `400` (inválido no formato), nunca uma decisão de fraude

### Requirement: Marcação de payload parcial na borda

A API SHALL enfileirar o caso mesmo quando o payload contém o `idSinistro` mas falta qualquer
outro campo do payload mínimo (apólice, aparelho, fotos ou metadados), marcando-o como
`payloadParcial`. A ausência desses campos não-estruturais MUST NOT bloquear o enfileiramento
— a incompletude é propagada como sinal de cobertura parcial para as features downstream.

#### Scenario: Falta de campo não-estrutural não impede o enfileiramento

- **WHEN** um cliente envia `POST /sinistros` com `idSinistro` presente mas sem o `aparelho`
- **THEN** a API enfileira o caso marcado como `payloadParcial` e responde `202` com um
  `caseId`
