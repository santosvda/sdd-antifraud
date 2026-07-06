# Arquitetura — Antifraude ACME (fundação)

Esta é a **fundação** (walking skeleton) do motor antifraude: o fluxo
`POST /sinistros → SQS → Worker → MySQL (caso + auditoria)` rodando ponta a ponta em
Docker, com os guardrails obrigatórios materializados em lugares rastreáveis. O motor de
score real é a fatia 1 (roadmap); aqui o `IScoreProvider` é um **mock sinalizado**.

## Visão geral (ports & adapters leve)

```
                    POST /sinistros                       long-poll
   cliente ───────────────────────►  [ Api ] ──enqueue──►  [ SQS ]  ──►  [ Worker ]
   (sistema de sinistros - mock)      valida             (LocalStack)      │
                                      202 + caseId                         │ MotorDeDecisao (Core)
                                                                          │  · resolve scoring_config ativa
                                                                          │  · score via IScoreProvider (mock)
                                                                          │  · faixa → rota (normal|reforçada)
                                                                          ▼
                                                    [ MySQL: casos + auditoria append-only ]
                                                    carimba versão da config e do provider
```

API e Worker **não se chamam direto** — conversam pela **fila** (entrada) e pelo **MySQL**
(estado). Isso desacopla recepção de processamento e é AWS-ready.

## Projetos

| Projeto | Papel | Depende de |
|---|---|---|
| `Antifraude.Core` | Domínio puro: entidades, portas (`IScoreProvider`, `IScoringConfigRepository`, `ICaseRepository`, `IAuditLog`) e o `MotorDeDecisao`. **Sem EF/SQS.** | — |
| `Antifraude.Infra` | Adapters: `DbContext` EF Core/MySQL, repositórios, `AuditLog`, cliente SQS, `MockScoreProvider`, composição de DI. | `Core` |
| `Antifraude.Api` | Minimal API: `POST /sinistros`, `GET /health`, Swagger. Valida na borda e enfileira. | `Core` + `Infra` |
| `Antifraude.Worker` | `BackgroundService`: consome o SQS e roda o pipeline de decisão. | `Core` + `Infra` |
| `Antifraude.Tests` | Unit (Core) + integração (Testcontainers: MySQL + LocalStack). | todos |

> **Regra de dependência:** `Core` é o centro e não conhece infraestrutura. Trocar o mock
> do `IScoreProvider` pelo motor determinístico (fatia 1) ou por ML (roadmap) não toca no
> resto. Os testes unitários do `Core` rodam sem subir nada.

## Fluxo ponta a ponta

1. **`POST /sinistros`** (`Api/Program.cs`) — valida o payload na borda, gera o `caseId`,
   publica o sinistro no SQS e responde `202 Accepted`. Nunca decide o mérito.
2. **SQS (LocalStack)** — desacopla recepção de processamento. Fila criada de forma
   idempotente no bootstrap.
3. **Worker** (`Worker/Worker.cs`) — long-poll na fila; para cada sinistro roda o
   `MotorDeDecisao`.
4. **`MotorDeDecisao`** (`Core/Decisao/MotorDeDecisao.cs`) — resolve a `scoring_config`
   ativa, obtém o score via `IScoreProvider`, classifica faixa/rota e produz um caso
   sempre roteado para fila humana + registro de auditoria.
5. **MySQL** — persiste o caso e a trilha de auditoria (append-only), correlacionados pelo
   `caseId`.

O `caseId` costura request → fila → worker → caso → auditoria (logs estruturados JSON com
o `caseId` no scope).

## Guardrails → onde moram

| Guardrail | Onde é materializado |
|---|---|
| **Nunca nega/aprova/bloqueia** | `MotorDeDecisao` só produz `score + faixa + rota`. Não há estado/endpoint de veredito. Enum `EstadoDoCaso` sem estado de bloqueio; pior caso = `PendenteRevisaoManual`. |
| **Human-in-the-loop** | Saída sempre roteada para fila humana (`Rota.Normal`/`Rota.Reforcada`) — `Classificador.RotaPara`. |
| **Não acusar o cliente** | Modelo de dados em termos de score/faixa/rota; sem campo "culpado/fraudador". |
| **Fail-open** | `MotorDeDecisao.FailOpen`: sinal faltante/parcial ou queda do provider → `PendenteRevisaoManual`, causa auditada, nunca lança. Mock tem modo "simular indisponibilidade". |
| **Auditoria imutável** | Tabela `auditoria` + triggers `BEFORE UPDATE`/`BEFORE DELETE` (`SIGNAL SQLSTATE`) na migration `AuditoriaImutavel`. Demonstrável: UPDATE/DELETE disparam erro. |
| **Config governada** | Tabela `scoring_config` versionada; versão ativa resolvida no cálculo e carimbada no caso e na auditoria. Nunca hard-coded, nunca env var. |

## Bootstrap e migrations

- A **API** aplica as migrations (`Database.Migrate()`) e semeia a `scoring_config` v1 no
  start, antes de ficar `healthy`; então garante a fila SQS.
- O **Worker** aguarda a fundação ficar pronta (banco migrado + config resolvível + fila)
  com retry/backoff, tolerando a ordem de subida dos containers.

## Como rodar

Ver `CLAUDE.md` (seção ".NET / Docker") para os comandos de build, run e teste.
