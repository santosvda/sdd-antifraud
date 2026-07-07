# containerized-environment Specification

## Purpose

Ambiente conteinerizado reproduzível: `docker compose up --build` sobe `api`, `worker`, `mysql` e `localstack` com healthchecks, aplica as migrations no start, é configurado por variáveis de ambiente versionadas em `.env.example`, e permite rodar a suíte de testes dentro do container.

## Requirements

### Requirement: Ambiente completo sobe com um comando

O sistema SHALL subir todos os serviços (`api`, `worker`, `mysql`, `localstack`) com um único `docker compose up --build`, sem passos manuais adicionais. Cada serviço MUST declarar healthcheck; a `api` MUST expor `GET /health`, o `mysql` MUST responder a `mysqladmin ping`, e o `localstack` MUST responder em `/_localstack/health`.

#### Scenario: Subida limpa a partir do repositório

- **WHEN** um desenvolvedor executa `docker compose up --build` num clone limpo com um `.env` derivado de `.env.example`
- **THEN** os quatro serviços iniciam e reportam healthy, e a API responde `200` em `GET http://localhost:8080/health`

#### Scenario: Derrubar o ambiente

- **WHEN** o desenvolvedor executa `docker compose down`
- **THEN** os containers são removidos, o volume nomeado do MySQL é preservado, e uma nova subida reencontra o estado anterior

### Requirement: Migrations aplicadas na inicialização

O sistema SHALL aplicar as migrations do EF Core (incluindo tabelas de caso, `scoring_config` e auditoria, e o trigger de imutabilidade) automaticamente no start, antes de a aplicação aceitar tráfego.

#### Scenario: Banco vazio na primeira subida

- **WHEN** o `mysql` sobe com volume vazio e a aplicação inicia
- **THEN** todas as tabelas e o trigger de auditoria existem antes de a `api` reportar healthy

### Requirement: Configuração via variáveis de ambiente versionadas por exemplo

O sistema SHALL ler connection string do MySQL, endpoint e credenciais fake do LocalStack, nome da fila SQS e região de variáveis de ambiente. Um `.env.example` MUST estar versionado com todas as chaves; o `.env` real MUST NOT ser commitado.

#### Scenario: `.env.example` cobre toda a configuração necessária

- **WHEN** um desenvolvedor copia `.env.example` para `.env` sem editar valores fake
- **THEN** o ambiente sobe funcional em modo local, e nenhum segredo real está presente no repositório

### Requirement: Testes executáveis dentro do container

O sistema SHALL permitir rodar a suíte de testes no ambiente conteinerizado via `docker compose run --rm api dotnet test`.

#### Scenario: Suíte roda em container

- **WHEN** o desenvolvedor executa `docker compose run --rm api dotnet test`
- **THEN** os testes unitários e de integração executam e o comando retorna código de saída refletindo o resultado
