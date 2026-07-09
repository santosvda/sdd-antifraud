# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## O que é

Motor **antifraude** da ACME (fraud-detection). A fundação (walking skeleton) já está no
ar: `POST /sinistros → SQS → Worker → MySQL (caso + auditoria)` rodando ponta a ponta em
Docker. O motor de score determinístico real é a **fatia 1** (roadmap); hoje o
`IScoreProvider` é um **mock sinalizado**. Visão de arquitetura completa em
[`ARCHITECTURE.md`](ARCHITECTURE.md); decisões técnicas em [`tech-specs.md`](tech-specs.md).

## Stack & estrutura

- **.NET 10 / C#** (package manager: `dotnet` / NuGet). Solution: `Antifraude.sln`.
- **Ports & adapters leve** — o domínio (`Core`) não conhece infraestrutura.

```
src/
  Antifraude.Core/    # domínio: entidades, portas, MotorDeDecisao — SEM EF/SQS
  Antifraude.Infra/   # adapters: EF Core/MySQL, cliente SQS, mock provider, DI
  Antifraude.Api/     # Minimal API: POST /sinistros, GET /health, Swagger
  Antifraude.Worker/  # BackgroundService: consome SQS → motor → grava caso + auditoria
tests/
  Antifraude.Tests/   # unit (Core) + integração (Testcontainers: MySQL + LocalStack)
```

- **Dependências:** `AWSSDK.SQS`, `Pomelo.EntityFrameworkCore.MySql` (EF Core 9),
  `Swashbuckle.AspNetCore`; testes com xUnit + FluentAssertions (v7) + coverlet +
  Testcontainers.

## Comandos

### Docker (forma canônica de rodar)

```bash
cp .env.example .env              # 1ª vez — valores fake já sobem tudo
docker compose up --build         # sobe api (8080) + worker + mysql (3306) + localstack (4566)
# API em http://localhost:8080  ·  Swagger em http://localhost:8080/swagger
# Testes no container (a imagem `api` é runtime-only — use a imagem SDK):
docker run --rm -v "$PWD":/src -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet test tests/Antifraude.Tests --filter "FullyQualifiedName~Unit"   # só unit
# Integração usa Testcontainers → precisa do socket do Docker + host override:
docker run --rm -v "$PWD":/src -w /src \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e TESTCONTAINERS_RYUK_DISABLED=true \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet test tests/Antifraude.Tests   # unit + integração
docker compose logs -f worker     # acompanha o processamento (logs JSON, correl. por caseId)
docker compose down               # derruba (volume nomeado do MySQL é preservado)
```

### .NET local (dev rápido)

```bash
dotnet build Antifraude.sln
dotnet test tests/Antifraude.Tests                              # toda a suíte
dotnet test tests/Antifraude.Tests --filter "FullyQualifiedName~Unit"        # só unit
dotnet test tests/Antifraude.Tests --filter "FullyQualifiedName~Integracao"  # só integração (precisa de Docker)
dotnet test --filter "FullyQualifiedName~MotorDeDecisaoTests"   # um teste/classe
dotnet format Antifraude.sln      # lint/format — sem erro antes do commit
```

> Os testes de integração usam **Testcontainers** e exigem Docker no host.

### EF Core migrations

```bash
dotnet tool install --global dotnet-ef        # 1ª vez
dotnet ef migrations add <Nome> --project src/Antifraude.Infra --startup-project src/Antifraude.Infra --output-dir Persistencia/Migrations
```

Migrations são aplicadas **no start** da API (antes de ficar healthy) — não rode
`database update` manualmente no fluxo normal.

## Convenções

- **API ↔ Worker só via SQS (entrada) + MySQL (estado)** — nunca chamada direta entre eles.
- **`Core` não referencia EF nem SQS.** Toda infra fica em `Infra`; a composição (DI) mora
  na borda (`Api`/`Worker`) via `AddAntifraudeInfra`.
- **Validação de entrada na borda** (`Api`); erro de domínio nunca derruba o caso — vira
  estado (`PendenteRevisaoManual`), ver fail-open.
- **Nomes de domínio em PT-BR** quando ajudam a rastrear o brief; termos técnicos em inglês.
- **Configuração por env var** (`.env`, nunca commitado; manter `.env.example`).
- **Testes:** xUnit + FluentAssertions; cobertura > 80% no `Core` é meta. Rodar `dotnet
  format` e `/simplify` antes de dar mudança como concluída.

## Guardrails (inegociáveis)

Nunca nega/aprova/bloqueia automático · human-in-the-loop obrigatório · auditoria imutável
(trigger no MySQL) · config de scoring governada e versionada · fail-open · não-discriminação.
Onde cada um mora: ver a tabela em [`ARCHITECTURE.md`](ARCHITECTURE.md#guardrails--onde-moram).
