## 1. Scaffold da solution

- [x] 1.1 Criar `Antifraude.sln` e os projetos `src/Antifraude.Core`, `src/Antifraude.Api`, `src/Antifraude.Worker`, `src/Antifraude.Infra` e `tests/Antifraude.Tests`
- [x] 1.2 Configurar referências: `Api` e `Worker` → `Core` + `Infra`; `Infra` → `Core`; `Tests` → todos. Garantir que `Core` NÃO referencia EF/SQS
- [x] 1.3 Adicionar pacotes: `AWSSDK.SQS` e `Pomelo.EntityFrameworkCore.MySql` no `Infra`; `Swashbuckle.AspNetCore` na `Api`; xUnit + FluentAssertions + coverlet + Testcontainers no `Tests`
- [x] 1.4 Adicionar `.gitignore` (.NET) e `.editorconfig`; confirmar `dotnet build` verde

## 2. Domínio em Core (ports & adapters)

- [x] 2.1 Definir entidades de domínio: `Caso` (com `caseId`, estado incluindo `PENDENTE_REVISAO_MANUAL`, rota `normal|reforçada`, faixa, score, versão da config, versão do provider) e o registro de auditoria
- [x] 2.2 Definir as portas: `IScoreProvider`, `IScoringConfigRepository`, `ICaseRepository`, `IAuditLog`
- [x] 2.3 Implementar o pipeline de decisão em `Core` (recebe sinais → obtém score via `IScoreProvider` → faixa/rota), sempre roteando para fila humana, nunca emitindo veredito
- [x] 2.4 Implementar lógica de fail-open: sinal faltante/parcial e exceção/timeout do provider → `PENDENTE_REVISAO_MANUAL`, causa capturada para auditoria, nunca lança para fora

## 3. Infra: persistência e mensageria

- [x] 3.1 Criar `DbContext` (Pomelo/MySQL) com tabelas de caso, `scoring_config` e auditoria; implementar `ICaseRepository`, `IScoringConfigRepository`, `IAuditLog`
- [x] 3.2 Criar migration inicial das tabelas
- [x] 3.3 Criar migration SQL dedicada com triggers `BEFORE UPDATE`/`BEFORE DELETE` na tabela de auditoria (`SIGNAL SQLSTATE`) bloqueando alteração/remoção
- [x] 3.4 Seed de uma versão ativa inicial de `scoring_config`; implementar resolução da versão ativa
- [x] 3.5 Implementar adapter SQS com `AWSSDK.SQS` (endpoint/credenciais/região por env var) e criação idempotente da fila no bootstrap
- [x] 3.6 Implementar `IScoreProvider` mock **sinalizado** (carimba versão do provider = mock) com modo "simular indisponibilidade" (lança/timeout sob demanda)

## 4. Api

- [x] 4.1 Configurar Minimal API com DI (Core + Infra), logs estruturados JSON e correlação por `caseId`
- [x] 4.2 Implementar `POST /sinistros`: validação na borda, publicação no SQS, resposta `202` com `caseId`; `400` para payload inválido — sem emitir veredito
- [x] 4.3 Implementar `GET /health` e habilitar Swagger em `/swagger`
- [x] 4.4 Aplicar migrations no start (antes de aceitar tráfego / healthy)

## 5. Worker

- [x] 5.1 Implementar `BackgroundService` com long-poll no SQS
- [x] 5.2 Ligar o consumo ao pipeline de decisão do `Core`: resolver `scoring_config` ativa, obter score, persistir caso roteado + auditoria, tudo correlacionado por `caseId`
- [x] 5.3 Garantir fail-open no consumo: falha do provider/sinal parcial → caso `PENDENTE_REVISAO_MANUAL` + auditoria da causa, sinistro nunca bloqueado
- [x] 5.4 Logs estruturados JSON com `caseId`

## 6. Ambiente Docker

- [x] 6.1 Criar `Dockerfile.api` e `Dockerfile.worker`
- [x] 6.2 Criar `compose.yaml` com `api` (8080), `worker`, `mysql` (3306, volume nomeado), `localstack` (4566) e healthchecks de cada serviço
- [x] 6.3 Criar `.env.example` (connection string MySQL, endpoint/credenciais fake LocalStack, nome da fila, região); garantir `.env` no `.gitignore`
- [x] 6.4 Validar `docker compose up --build` ponta a ponta: `POST /sinistros` → SQS → Worker → caso + auditoria no MySQL

## 7. Testes

- [x] 7.1 Unit no `Core`: faixa/rota/roteamento e fail-open determinísticos (meta cobertura > 80%)
- [x] 7.2 Teste de integração: trigger bloqueia UPDATE e DELETE na auditoria (dispara erro)
- [x] 7.3 Teste de integração (Testcontainers, MySQL + LocalStack): fluxo de fumaça `POST /sinistros` → caso roteado persistido
- [x] 7.4 Teste de não-bloqueio: derrubar o mock do `IScoreProvider` de propósito → caso `PENDENTE_REVISAO_MANUAL`, sinistro segue
- [x] 7.5 `dotnet format` sem erros; rodar `/simplify` antes de concluir

## 8. Repositório e CI

- [x] 8.1 Criar workflow GitHub Actions rodando `dotnet build` + `dotnet test` em cada PR
- [x] 8.2 Repo remoto provisionado via fork: `origin` = `santosvda/antiFraude` (fork de `upstream` `danielhs73/antiFraude`, default `main`); branch `bootstrap-foundation` pushada para `origin`

## 9. Documentação

- [x] 9.1 Criar `ARCHITECTURE.md` com a visão de arquitetura (ports & adapters, fluxo ponta a ponta, guardrails → onde moram)
- [x] 9.2 Estender `CLAUDE.md` com convenções .NET: comandos de run/test no Docker, estrutura de pastas, package manager (`dotnet`/NuGet), regra api↔worker só via SQS+MySQL
