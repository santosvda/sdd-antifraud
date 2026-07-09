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

## Ingestão real do sinistro (Feature 2.1)

A borda recebe o **payload de sinistro real** (não mais um `sinais[]` placeholder):
`idSinistro` (único campo estrutural), `apolice`, `aparelho` (IMEI/série), `fotos` (por
referência), `metadados`. A ingestão é idempotente e resiliente:

- **Idempotência** — `POST /sinistros` checa o `idSinistro` num store de dedup
  (`sinistros_processados`, tabela MySQL, TTL de 24h) antes de enfileirar; duplicado dentro da
  janela é descartado com log. Se o store cai, é **fail-open** (processa + alerta). Purga
  periódica via `PurgaDedupService`.
- **Payload parcial** — falta de qualquer campo exceto `idSinistro` não bloqueia: o caso segue
  marcado como `payloadParcial`.
- **Fila de erro técnico** — evento sem `idSinistro` (não-processável) → `202` + fila de erro
  técnico (nunca `400`; o sinistro já existe no sistema principal). Só corpo ilegível vira `400`.
- **Retry/backoff** — o enfileiramento tenta com backoff (~1s/4s/16s); esgotado, escala para a
  fila de erro técnico. Se o broker está totalmente fora, a API responde `503` (exceção
  deliberada: força o produtor a reenviar).
- **Auditoria da ingestão** — tabela append-only `auditoria_ingestao` (mesmos triggers de
  imutabilidade) registra completude do payload, resultado da idempotência e destino do
  roteamento.

## Coleta de sinais (Feature 2.2)

Antes da decisão, o Worker coleta os **3 sinais fixos** desta fatia via `ColetorDeSinais`
(`Core/Coleta`), em **paralelo** e com isolamento por sinal:

- **`reuso_imagem`** — pHash (64 bits) das fotos comparado ao histórico de 6 meses;
  reuso confirmado com distância de Hamming ≤ 10. Sem acesso aos bytes nesta fatia, o
  hash é derivado da referência da foto e **sinalizado** `phash-fake-v1` (mesmo padrão do
  mock de score).
- **`imei_serie_divergente`** — IMEI/série do sinistro × base de apólices; ativa tanto
  para "diverge" quanto para "não cadastrado" (a evidência distingue os motivos).
- **`velocity`** — ≥2 sinistros do mesmo cliente OU aparelho em 90 dias (janela a partir
  de `abertoEm`, fallback data de processamento). O caso nunca se conta: as consultas
  excluem o próprio `idSinistro` e o registro no histórico é upsert idempotente.

Cada sinal é **tri-estado** (`Ativo`/`Inativo`/`Indisponivel`) com evidência estruturada
e mascarada (IMEI/série truncados). Dado ausente no payload ou fonte fora do ar viram
`Indisponivel` (nunca "falso"), com o motivo auditado. As 3 fontes (tabelas locais fake:
`imagem_hashes`, `apolices`, `historico_sinistros`) ficam atrás de portas do Core com
decorators de **timeout + circuit breaker independentes** (`CircuitoDaFonte`), e cada uma
tem modo "simular indisponibilidade" via env var (`FONTE_*_INDISPONIVEL`).

> Indisponibilidade **parcial** (1–2 sinais) segue para o score com os sinais disponíveis
> e `DadosIncompletos = true`; com **3/3 indisponíveis** o caso é "não avaliado" e cai no
> fail-open (`PendenteRevisaoManual`). O peso de `velocity` na `scoring_config` é decisão
> de calibração da 2.3 — o sinal já nasce calculado e auditado.

## Fluxo ponta a ponta

1. **`POST /sinistros`** (`Api/Program.cs`) — valida o formato mínimo, aplica idempotência,
   gera o `caseId`, publica o sinistro no SQS (com retry) e responde `202 Accepted`. Nunca
   decide o mérito.
2. **SQS (LocalStack)** — duas filas (processamento + erro técnico) criadas de forma
   idempotente no bootstrap. Desacopla recepção de processamento.
3. **Worker** (`Worker/Worker.cs`) — long-poll na fila; para cada sinistro coleta os 3
   sinais (`ColetorDeSinais`, feature 2.2) e roda o `MotorDeDecisao` com o sinistro
   enriquecido.
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
| **Fail-open** | `MotorDeDecisao.FailOpen`: nenhum sinal calculável (vazio ou 3/3 indisponíveis) ou queda do provider → `PendenteRevisaoManual`, causa auditada, nunca lança. Indisponibilidade parcial segue com score + `DadosIncompletos`. Mock e fontes têm modo "simular indisponibilidade". |
| **Auditoria imutável** | Tabela `auditoria` + triggers `BEFORE UPDATE`/`BEFORE DELETE` (`SIGNAL SQLSTATE`) na migration `AuditoriaImutavel`. Demonstrável: UPDATE/DELETE disparam erro. |
| **Config governada** | Tabela `scoring_config` versionada; versão ativa resolvida no cálculo e carimbada no caso e na auditoria. Nunca hard-coded, nunca env var. |

## Bootstrap e migrations

- A **API** aplica as migrations (`Database.Migrate()`) e semeia a `scoring_config` v1 no
  start, antes de ficar `healthy`; então garante a fila SQS.
- O **Worker** aguarda a fundação ficar pronta (banco migrado + config resolvível + fila)
  com retry/backoff, tolerando a ordem de subida dos containers.

## Como rodar

Ver `CLAUDE.md` (seção ".NET / Docker") para os comandos de build, run e teste.
