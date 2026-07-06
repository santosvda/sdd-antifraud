## Why

O motor antifraude da ACME precisa de uma fundação técnica **rodando de ponta a ponta em containers** antes de receber a fatia de produto (o motor de score determinístico). Sem esse esqueleto — API que enfileira, worker que consome, MySQL com auditoria imutável, LocalStack como SQS — os guardrails obrigatórios (human-in-the-loop, não-bloqueio, auditoria imutável, config governada, fail-open) não têm onde morar de forma rastreável. Esta mudança levanta esse terreno.

## What Changes

- Cria a solution .NET `Antifraude.sln` com quatro projetos (`Api`, `Worker`, `Core`, `Infra`) + projeto de testes, em arranjo ports & adapters leve (`Core` sem dependência de EF/SQS).
- **Walking skeleton ponta a ponta:** `POST /sinistros` valida na borda, enfileira no SQS (LocalStack) e responde `202`; o `Worker` (`BackgroundService`) consome a fila, chama um `IScoreProvider` **placeholder sinalizado como mock**, e grava um caso sempre roteado para fila humana + trilha de auditoria — **nunca** nega/aprova/bloqueia.
- Sobe o ambiente completo com `docker compose up --build`: `api`, `worker`, `mysql`, `localstack`, com healthchecks e migrations aplicadas no start.
- Materializa o esqueleto dos guardrails: tabela de auditoria **append-only com trigger que bloqueia UPDATE/DELETE**, e tabela `scoring_config` **versionada** cuja versão ativa é carimbada no caso.
- Estabelece o estado de **fail-open** (`PENDENTE_REVISAO_MANUAL`) como destino de sinal faltante ou queda do `IScoreProvider` — o caso sempre nasce e fica visível.
- Cria o repositório remoto no GitHub (`main` deployável) com **CI mínimo** (`dotnet build` + `dotnet test` no PR).
- Cria `ARCHITECTURE.md` e estende o `CLAUDE.md` com as convenções .NET (comandos de run/test no Docker, estrutura de pastas, ports & adapters).

**Fora do escopo desta mudança (fatia 1, follow-up):** o motor de regras determinístico real (soma ponderada de sinais, faixas por limiar), a recusa de atributos sensíveis, e os cenários Gherkin de aceite completos. Aqui o `IScoreProvider` é um placeholder mockado e sinalizado.

## Capabilities

### New Capabilities

- `containerized-environment`: um único `docker compose up --build` sobe api + worker + mysql + localstack com healthchecks, volume nomeado para o MySQL, migrations no start e `.env.example` versionado; testes rodam em container.
- `claim-intake-api`: `POST /sinistros` valida a entrada na borda, enfileira no SQS e responde `202` sem esperar o processamento; expõe `GET /health` e Swagger.
- `claim-processing-worker`: `BackgroundService` consome o SQS, invoca o `IScoreProvider`, persiste o caso roteado para fila humana e registra a auditoria; comportamento fail-open quando sinal falta ou o provider cai.
- `immutable-audit-trail`: trilha append-only em MySQL com trigger que bloqueia UPDATE/DELETE; cada caso carimba sinais+origem, versão da `scoring_config`, versão do provider, rota, timestamp e ator.
- `scoring-config-store`: tabela `scoring_config` versionada no banco (nunca hard-coded, nunca env var); a versão ativa é resolvida no processamento e carimbada no caso.
- `github-repo-ci`: repositório privado no GitHub com branch default `main` e workflow de CI que roda `dotnet build` + `dotnet test` em cada PR.

### Modified Capabilities

<!-- Nenhuma — não há specs existentes; esta é a fundação. -->

## Impact

- **Código:** repositório hoje vazio (só `README.md`) — passa a conter a solution .NET completa, Dockerfiles, `compose.yaml`, `.env.example`, workflow de CI, `ARCHITECTURE.md` e `CLAUDE.md` estendido.
- **Dependências novas:** `AWSSDK.SQS`, `Pomelo.EntityFrameworkCore.MySql`, `Swashbuckle.AspNetCore`; testes com xUnit + FluentAssertions + coverlet + Testcontainers.
- **Infra:** Docker/Docker Compose obrigatório para rodar; nenhum recurso de nuvem provisionado (só LocalStack local).
- **Sistemas externos:** cria repositório remoto no GitHub via `gh`.
- **Contrato para a fatia 1:** as interfaces de `Core` (`IScoreProvider`, `IScoringConfigRepository`, `ICaseRepository`, `IAuditLog`) ficam estáveis; a fatia 1 substitui o mock do `IScoreProvider` pelo motor determinístico sem tocar no resto.
