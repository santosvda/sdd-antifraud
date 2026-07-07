# claim-intake-api Specification

## Purpose

Recepção de sinistros pela borda HTTP: o endpoint `POST /sinistros` valida o formato, enfileira no SQS e responde `202 Accepted` com um `caseId` de correlação, sem decidir o mérito do sinistro. Expõe também `GET /health` e a documentação OpenAPI em `/swagger`.

## Requirements

### Requirement: Recepção assíncrona do sinistro

O endpoint `POST /sinistros` SHALL validar a entrada na borda, enfileirar o sinistro no SQS e responder `202 Accepted` sem esperar o processamento. A resposta MUST conter um identificador de correlação (`caseId`) que acompanha o sinistro por todo o fluxo.

#### Scenario: Sinistro válido é aceito

- **WHEN** um cliente envia `POST /sinistros` com um payload válido de sinistro e sinais
- **THEN** a API enfileira a mensagem no SQS e responde `202` com um `caseId`

#### Scenario: Payload inválido é rejeitado na borda

- **WHEN** um cliente envia `POST /sinistros` com payload malformado ou faltando campos obrigatórios
- **THEN** a API responde `400` com detalhe do erro e NÃO enfileira nada

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
