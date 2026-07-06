# Spec Técnica — Antifraude ACME (fundação + fatia 1)

> Trilha: **C · Antifraude**. Dono: **dev/arquiteto**.
> Origem: `briefs/antifraude.md` + framing (tema + fatia) + guardrails do `CLAUDE.md`.
> Esta spec nasceu de um `/grill-me` decisão a decisão. Cada linha aqui é rastreável — o código aponta para ela.

## 0. Framing da fatia (loop 1)

**Fatia:** *núcleo decisório do motor antifraude* — dado um sinistro com sinais coletados, o sistema **combina 2–3 sinais em um score de risco**, **classifica** o caso em baixo/médio/alto e **roteia** para fila normal ou reforçada, **sem nunca bloquear, negar ou aprovar** o sinistro automaticamente.

É a fatia que mais expõe o guardrail central (human-in-the-loop obrigatório, não-bloqueio) e a decisão de negócio mais sensível (limiares configuráveis e sua governança).

**Fora desta fatia:** coleta real de sinais (hash perceptual, EXIF — entram como inputs mockados), painel do analista, retreino do modelo por feedback, ML real, deploy AWS.

**Tem UI?** Não no loop 1 — **API-only** (Swagger/JSON). React entra no loop 2 (painel do analista).

## 1. Objetivo da fundação

Levantar um esqueleto **rodando em container** e um **repositório remoto no GitHub**, pronto para receber a fatia 1. A base técnica já prepara o terreno para os guardrails obrigatórios (auditoria imutável, decisão humana, config governada, fail-open).

## 2. Stack escolhida

| Camada | Escolha | Razão (1 linha) |
|---|---|---|
| **Linguagem/runtime** | .NET (C#) | menor cerimônia para caber a fatia ponta a ponta em ~6h; proficiência do squad |
| **Backend / API** | ASP.NET Core Minimal API | recebe o sinistro, valida, enfileira e responde `202` sem esperar o processamento |
| **Worker** | `BackgroundService` (hosted service nativo) | consumo assíncrono da fila sem trazer framework extra |
| **Fila / mensageria** | AWS SQS via **LocalStack** | assíncrono real com SDK AWS; código AWS-ready sem provisionar nuvem |
| **Banco / persistência** | MySQL + EF Core | proficiência do squad; ORM maduro |
| **IA / score** | Motor de regras **determinístico** atrás de `IScoreProvider` | explicabilidade e auditoria saem de graça; **sem número inventado** |
| **Frontend** | — (loop 1 API-only) | React fica para o loop 2 |
| **Nuvem** | Só local via LocalStack | valor do workshop está no motor/guardrails, não em infra |

> A "IA que computa o score" do brief entra nesta fatia como **mock explícito e sinalizado** (`IScoreProvider`). O motor de regras é o coração; o ML real fica atrás da mesma interface, como roadmap — trocar não mexe no resto.

## 3. Bibliotecas & patterns

- **Libs principais:**
  - `AWSSDK.SQS` — cliente da fila (aponta para o endpoint do LocalStack em dev).
  - `Pomelo.EntityFrameworkCore.MySql` (ou `MySql.EntityFrameworkCore`) — EF Core sobre MySQL.
  - `Swashbuckle.AspNetCore` — Swagger/OpenAPI (a "UI" da fatia API-only).
  - Testes: **xUnit** + **FluentAssertions** + **coverlet** + **Testcontainers** (integração).
- **Patterns / arquitetura:** ports & adapters leve. `Antifraude.Core` não conhece EF nem SQS — só interfaces (`IScoreProvider`, `IScoringConfigRepository`, `ICaseRepository`, `IAuditLog`). Infra implementa os adapters.
- **Convenções:**
  - Api e worker **não se chamam direto** — conversam pela **fila** (entrada) e pelo **MySQL** (estado).
  - Validação de entrada na borda (Api); erro de domínio nunca derruba o caso (ver fail-open).
  - Nomes de domínio em PT-BR quando ajudam a rastrear o brief; termos técnicos em inglês.

## 4. Estrutura do projeto

```
Antifraude.sln
src/
  Antifraude.Api/         # POST /sinistros → valida → enfileira SQS → 202
  Antifraude.Worker/      # BackgroundService: consome SQS → motor → grava caso + auditoria
  Antifraude.Core/        # domínio: motor de regras, IScoreProvider, entidades, config (SEM infra)
  Antifraude.Infra/       # EF Core/MySQL, cliente SQS, repositórios, IAuditLog
tests/
  Antifraude.Tests/       # unit (Core) + integração (Gherkin do PRD, via Docker)
Dockerfile.api
Dockerfile.worker
compose.yaml
.env.example
```

`Antifraude.Core` é 100% testável em unit sem subir nada. Os guardrails (auditoria, config versionada, fail-open) moram em lugares óbvios e rastreáveis.

## 5. Ambiente Docker (obrigatório)

Um `docker compose up --build` levanta tudo.

**Serviços do `compose.yaml`:**

| Serviço | Papel | Porta | Healthcheck |
|---|---|---|---|
| `api` | ASP.NET Core Minimal API | `8080` | `GET /health` |
| `worker` | BackgroundService (consumidor SQS) | — | liveness do processo |
| `mysql` | persistência + auditoria | `3306` | `mysqladmin ping` |
| `localstack` | SQS local (stand-in AWS) | `4566` | `curl /_localstack/health` |

- **Subir:** `docker compose up --build` · **Derrubar:** `docker compose down`
- **Testes no container:** `docker compose run --rm api dotnet test`
- **Variáveis** (`.env`, nunca commitado; manter `.env.example`): connection string MySQL, endpoint/credenciais fake do LocalStack, nome da fila SQS, região.
- **Volumes:** volume nomeado para o MySQL; migrations aplicadas no start.

## 6. Repositório remoto

**GitHub.**

```bash
gh repo create acme-antifraude --private --source=. --remote=origin --push
```

- Branch default: `main` (estável e deployável).
- Branches short-lived, uma mudança por branch; commits atômicos.
- **CI mínimo** (GitHub Actions): `dotnet build` + `dotnet test` no PR.

## 7. Qualidade & tooling

- **Testes:** TDD; xUnit + FluentAssertions; **cobertura > 80%** via coverlet; todos verdes antes de "pronto".
- **Dois níveis:**
  1. **Unit** no motor de regras (`Core`) — score/faixa/roteamento determinístico, fácil de cobrir >80%.
  2. **Integração/aceite** — fluxo `POST /sinistros` → SQS → worker → caso roteado, usando MySQL + LocalStack do compose (Testcontainers). Os cenários Gherkin do PRD **são** estes testes.
- **Lint/format:** `dotnet format` — sem erro antes do commit.
- **Polish:** rodar `/simplify` antes de dar qualquer mudança como concluída.

## 8. Observabilidade

- Logs estruturados desde o início (JSON), com correlação por `caseId`.
- Inspeção: `docker compose logs -f worker`.
- Medir o RNF **alerta ≤ 5 min p95** (o motor determinístico resolve em ms; a métrica existe para não regredir).
- Rastreabilidade ponta a ponta: request → fila → worker → caso → auditoria compartilham o mesmo `caseId`.

## 9. Rastreabilidade dos guardrails → fundação

| Guardrail obrigatório (brief/CLAUDE.md) | Como a fundação o materializa |
|---|---|
| **Nunca nega/aprova/bloqueia automático** | O motor só produz `score + faixa + rota`. Não existe endpoint/estado que negue ou aprove o sinistro. Pior caso = `PENDENTE_REVISAO_MANUAL`. |
| **Human-in-the-loop obrigatório** | A saída do worker é sempre um caso roteado para uma **fila humana** (normal/reforçada). Nenhuma ação final é automática. |
| **Não acusar o cliente** | Saída em termos de score/indício/justificativa; sem campo/estado de "culpado" ou "fraudador". |
| **Não-discriminação** | O motor recusa sinais/atributos sensíveis proibidos (lista explícita); config de score não os admite. Teste garante rejeição. |
| **Fail-open** | Estado `PENDENTE_REVISAO_MANUAL` é o destino de sinal faltando/parcial e de queda do `IScoreProvider`. Caso **sempre nasce e é visível**; nunca trava/some. |
| **Trilha de auditoria imutável** | Tabela append-only + **trigger que bloqueia UPDATE/DELETE**. Cada caso carimba: sinais+origem, score, faixa, versão da `scoring_config`, versão do provider, prompt (quando aplicável), rota, timestamp, ator. Demonstrável: um UPDATE dispara erro. |
| **Limiares configuráveis, sem env var** | Tabela `scoring_config` versionada no MySQL; a versão ativa gera o score e é **carimbada no caso** ("score veio da config v3"). Nunca hard-coded, nunca env var. |
| **LGPD (minimização, base legal, acesso restrito)** | Persistir só o necessário à decisão; base legal = prevenção à fraude (documentar no PRD); acesso segregado (analista vs. compliance) previsto no schema. |

## 10. Motor de score (coração da fatia)

- **Determinístico:** score = soma ponderada dos sinais presentes. Cada parcela é rastreável (ex.: `+30 reuso de imagem`, `+25 IMEI×série divergente`).
- **Faixa por limiar** configurável (baixo/médio/alto) → **rota** (normal/reforçada).
- **`IScoreProvider`** é a costura: hoje = implementação de regras; ML real no futuro sem tocar no resto.
- **Explicabilidade** é subproduto: a lista de parcelas + a versão da config **é** a justificativa auditável.
- **Sem número inventado:** sinais chegam mockados e **sinalizados como mock**; nenhum valor fabricado em caminho real.

## 11. Fail-open — comportamento em código

- **Sinais faltando/parciais:** motor calcula com o que tem, marca `score parcial / dados incompletos`, roteia para **revisão manual**; a ausência vai para a auditoria. Não assume score baixo nem alto por omissão.
- **`IScoreProvider` lança/timeout:** worker captura, cria o caso com faixa "indeterminado → revisão manual", registra a falha na trilha. O caso **existe e é visível**.
- **Em nenhum ramo** o sistema rejeita/bloqueia o sinistro. O mock ganha um modo "simular indisponibilidade" e um **teste de aceite derruba o mock de propósito** para provar o não-bloqueio.

## 12. Fluxo (ponta a ponta)

```
[sistema de sinistros - MOCK] → POST /sinistros
        │  (valida, enfileira SQS, responde 202)
        ▼
   [ Api ] ──enqueue──► [ SQS (LocalStack) ] ──► [ Worker ]
                                                    │ lê scoring_config vigente
                                                    │ motor determinístico → score + faixa
                                                    │ roteia (normal | reforçada) — SEM bloquear
                                                    ├─► [ IScoreProvider - MOCK sinalizado ]
                                                    ▼
                                    [ MySQL: casos + auditoria append-only ]
                                    (carimba versão da config e do provider)
```

## 13. Critérios de aceite (Gherkin) → viram testes de integração

Cobrir no mínimo:

- **Reuso de imagem** detectado → indício listado com evidência.
- **Inconsistência IMEI × série** → indício listado.
- **Score alto** → roteia para fila reforçada **sem bloquear** o sinistro.
- **IA (`IScoreProvider`) indisponível** → caso vai para `PENDENTE_REVISAO_MANUAL` (fail-open), sinistro **segue**.
- **Nenhum caso** é bloqueado/negado/aprovado automaticamente.
- **Atributo sensível proibido** enviado como sinal → **rejeitado** (não-discriminação).

## 14. Como rodar (resumo)

```bash
docker compose up --build         # sobe api + worker + mysql + localstack
# API em http://localhost:8080  (Swagger em /swagger)
docker compose run --rm api dotnet test   # roda unit + integração no container
docker compose logs -f worker     # acompanha o processamento
docker compose down               # derruba
```

## 15. Fora de escopo do loop 1 (roadmap)

- Coleta real de sinais (pHash, EXIF, correlação) — hoje mockados e sinalizados.
- Painel do analista em **React** (loop 2).
- Feedback loop → retreino do modelo.
- Modelo de **ML real** atrás do `IScoreProvider`.
- Deploy na **AWS** (código já é AWS-ready via LocalStack).
- Hash encadeado na auditoria (reforço de imutabilidade além do trigger).

---

## Decisões consolidadas (do `/grill-me`)

| # | Decisão | Escolha |
|---|---|---|
| 1 | Backend/worker | .NET (C# / ASP.NET Core Minimal API + `BackgroundService`) |
| 2 | Fila | AWS SQS via LocalStack |
| 3 | Banco | MySQL + EF Core |
| 4 | Auditoria | Tabela append-only + trigger que bloqueia UPDATE/DELETE |
| 5 | UI | Sem React no loop 1 (API-only) |
| 6 | Score | Motor de regras determinístico atrás de `IScoreProvider` |
| 7 | Config | Tabela `scoring_config` versionada, versão carimbada no caso |
| 8 | Fail-open | Estado `PENDENTE_REVISAO_MANUAL` + mock que simula queda + teste de não-bloqueio |
| 9 | Repo | GitHub + CI mínimo (`dotnet build/test`) |
| 10 | Nuvem | Só local via LocalStack; deploy AWS fora de escopo |
| 11 | Testes | xUnit + FluentAssertions + coverlet; Gherkin como integração no container |
| 12 | Layout | Solution `Api / Worker / Core / Infra / Tests`; api↔worker só via SQS + MySQL |

> **Ao executar esta spec:** criar `ARCHITECTURE.md` com a visão de arquitetura e **estender o `CLAUDE.md`** com as convenções .NET (package manager: `dotnet`/NuGet; comandos de run/test no Docker; estrutura de pastas). A spec técnica é a **primeira a ser executada** (`/opsx:apply`) — levanta a fundação antes da fatia de produto.
