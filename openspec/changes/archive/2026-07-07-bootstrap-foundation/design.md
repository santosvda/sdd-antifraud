## Context

O repositório está vazio (só `README.md`). A `tech-specs.md` consolidou, decisão a decisão via `/grill-me`, a stack e a arquitetura da fundação do motor antifraude ACME. Esta mudança implementa o **walking skeleton**: o fluxo `POST /sinistros → SQS → Worker → MySQL (caso + auditoria)` rodando ponta a ponta em Docker, com os guardrails obrigatórios materializados em lugares rastreáveis, mas com o motor de score ainda como mock sinalizado (o motor determinístico real é a fatia 1).

Restrições que moldam o design:
- Cabe em ~6h de workshop → menor cerimônia possível; .NET pela proficiência do squad.
- Guardrails são inegociáveis: nunca nega/aprova/bloqueia, human-in-the-loop, auditoria imutável, config governada, fail-open, não-discriminação.
- Código **AWS-ready** sem provisionar nuvem → SQS real via SDK apontando para LocalStack.
- Rastreabilidade: cada linha da spec aponta para código; o `caseId` costura request → fila → worker → caso → auditoria.

## Goals / Non-Goals

**Goals:**
- Solution .NET com 4 projetos em ports & adapters leve: `Core` (domínio puro, sem EF/SQS), `Infra` (adapters), `Api`, `Worker`.
- Fluxo ponta a ponta funcional com `IScoreProvider` mock sinalizado.
- Guardrails estruturais no lugar: trigger de imutabilidade da auditoria, `scoring_config` versionada e carimbada, estado `PENDENTE_REVISAO_MANUAL` como fail-open.
- `docker compose up --build` sobe api + worker + mysql + localstack; migrations no start; testes rodáveis em container.
- Repo GitHub privado + CI mínimo (`dotnet build`/`dotnet test`).
- `ARCHITECTURE.md` + `CLAUDE.md` estendido com convenções .NET.

**Non-Goals:**
- Motor de regras determinístico real (soma ponderada, faixas por limiar calibradas) — fatia 1.
- Recusa de atributos sensíveis com lista explícita e teste — fatia 1 (aqui só o ponto de extensão existe).
- Cenários Gherkin de aceite completos como testes de integração — fatia 1; aqui um teste de fumaça do fluxo basta.
- Coleta real de sinais (pHash/EXIF), painel React, ML real, deploy AWS, hash encadeado na auditoria — roadmap.

## Decisions

**1. Ports & adapters leve, `Core` sem infra.**
`Antifraude.Core` define entidades de domínio e as portas `IScoreProvider`, `IScoringConfigRepository`, `ICaseRepository`, `IAuditLog`. `Antifraude.Infra` implementa os adapters (EF Core/MySQL, cliente SQS). `Api` e `Worker` referenciam `Core` + `Infra` e fazem a composição (DI) na borda.
*Por quê:* mantém o coração (score/roteamento/fail-open) 100% testável em unit sem subir nada, e permite trocar o mock do `IScoreProvider` pelo motor real (fatia 1) e por ML (roadmap) sem tocar no resto. *Alternativa descartada:* camadas por infra-first (DbContext no centro) — acoplaria o domínio ao EF e dificultaria os testes unitários rápidos.

**2. Api e Worker se comunicam só por SQS (entrada) e MySQL (estado); nunca chamada direta.**
`POST /sinistros` valida, publica na fila e responde `202`. O Worker (`BackgroundService`) faz long-poll no SQS.
*Por quê:* desacopla recepção de processamento (a API responde sem esperar), é assíncrono real e AWS-ready. *Alternativa descartada:* chamada HTTP direta api→worker — acoplaria os serviços e quebraria o assíncrono do brief.

**3. SQS via AWS SDK apontando para LocalStack.**
`AWSSDK.SQS` com `ServiceURL` = endpoint do LocalStack (`http://localstack:4566`) e credenciais fake, tudo por env var. A fila é criada no bootstrap (idempotente) para não depender de setup manual.
*Por quê:* o código é idêntico ao de produção AWS — só o endpoint muda. *Alternativa descartada:* RabbitMQ/mensageria in-memory — não seria AWS-ready e traria framework extra.

**4. MySQL + EF Core (Pomelo), migrations aplicadas no start.**
`Pomelo.EntityFrameworkCore.MySql`. Na inicialização, a aplicação roda `Database.Migrate()` antes de aceitar tráfego; o healthcheck da API só fica verde depois disso.
*Por quê:* proficiência do squad, ORM maduro, subida reproduzível sem passo manual. *Trade-off:* migrate-on-start é simples mas serializa a subida — aceitável no escopo local.

**5. Auditoria imutável por trigger de banco, não por convenção de aplicação.**
Tabela append-only + triggers `BEFORE UPDATE`/`BEFORE DELETE` que fazem `SIGNAL SQLSTATE` (erro), criados numa migration dedicada (SQL cru, já que EF não modela triggers).
*Por quê:* imutabilidade garantida no nível mais baixo — nem um bug de aplicação nem acesso direto ao banco conseguem alterar a trilha; é **demonstrável** (um UPDATE dispara erro). *Alternativa descartada:* imutabilidade só na camada de repositório — confiável demais na disciplina do código; não resiste a acesso direto.

**6. `scoring_config` versionada no banco, versão ativa carimbada no caso.**
Tabela com versão + payload de pesos/limiares; uma versão marcada como ativa. O Worker resolve a versão ativa no momento do cálculo e grava o número da versão no caso e na auditoria. Nunca hard-coded, nunca env var.
*Por quê:* limiares são a decisão de negócio mais sensível e precisam de governança e rastreabilidade ("o score veio da config v3"). Casos antigos preservam a versão que os originou.

**7. `IScoreProvider` mock explícito e sinalizado, com modo "simular queda".**
Na fundação, a implementação retorna um score placeholder e **carimba na auditoria que veio de um mock** (versão do provider = mock). Expõe um modo que lança/timeout sob demanda.
*Por quê:* "sem número inventado em caminho real" — o mock é sempre sinalizado; e o modo de queda alimenta o teste de não-bloqueio (fail-open). *Alternativa descartada:* retornar score fixo silenciosamente — violaria a rastreabilidade e mascararia o mock.

**8. Fail-open como estado de domínio: `PENDENTE_REVISAO_MANUAL`.**
Sinal faltante/parcial, ou exceção/timeout do `IScoreProvider`, levam o Worker a criar o caso nesse estado, registrar a causa na auditoria e rotear para revisão humana. Em nenhum ramo o sinistro é rejeitado/bloqueado. Erro de domínio nunca derruba o caso — é capturado e vira estado.
*Por quê:* o caso **sempre nasce e fica visível**; a falha é auditada, não engolida.

**9. Roteamento sempre para fila humana (normal | reforçada); sem veredito automático.**
A saída do Worker é `score + faixa + rota`. Não existe endpoint nem estado que negue, aprove ou bloqueie; o pior caso é revisão manual. Sem campo "culpado/fraudador".
*Por quê:* materializa human-in-the-loop e não-acusação diretamente no modelo de dados.

**10. Observabilidade: logs estruturados JSON correlacionados por `caseId` desde o início.**
Métrica de latência alerta ≤ 5 min p95 existe como guarda contra regressão (o motor determinístico resolve em ms).

**11. Testes: xUnit + FluentAssertions + coverlet; integração via Testcontainers.**
Unit no `Core` (score/faixa/roteamento/fail-open determinísticos, fácil >80%). Integração sobe MySQL + LocalStack via Testcontainers e exercita o fluxo. Na fundação: um teste de fumaça do fluxo ponta a ponta + teste de que o trigger bloqueia UPDATE/DELETE + teste de não-bloqueio derrubando o mock. Cobertura >80% é meta.

## Risks / Trade-offs

- **Migrate-on-start com múltiplas réplicas poderia colidir** → No escopo local há uma instância de cada serviço; para produção, mover migrations para um passo de deploy dedicado (fora de escopo).
- **Trigger de imutabilidade é específico do MySQL** → Aceito: o banco é fixo por decisão; a migration SQL fica isolada e documentada.
- **Testcontainers exige Docker no host de CI e é mais lento** → Mitigar mantendo poucos testes de integração (fluxo + guardrails-chave) e o grosso da cobertura em unit no `Core`.
- **Mock do `IScoreProvider` poderia vazar para caminho real sem sinalização** → Mitigar com o carimbo obrigatório de "versão do provider = mock" na auditoria e um teste que verifica a sinalização.
- **LocalStack e o setup da fila podem introduzir flakiness na subida** → Criação idempotente da fila no bootstrap + healthchecks antes de a API aceitar tráfego.

## Migration Plan

1. Scaffold da solution e projetos; portas em `Core`.
2. `Infra`: DbContext + entidades + migrations (incluindo a migration SQL do trigger de auditoria) + adapter SQS + repositórios + `IAuditLog`.
3. `Api` (endpoints + validação + Swagger + `/health`) e `Worker` (`BackgroundService` + pipeline de decisão + fail-open).
4. Dockerfiles + `compose.yaml` + `.env.example`; validar `docker compose up --build` ponta a ponta.
5. Testes (unit + fumaça de integração) verdes; `dotnet format` limpo.
6. `ARCHITECTURE.md` + extensão do `CLAUDE.md`.
7. Criar repo GitHub + workflow de CI.

*Rollback:* mudança é aditiva sobre repo vazio; reverter = descartar a branch. Nenhum dado de produção envolvido.

## Open Questions

- Nome exato do provider/pacote EF: `Pomelo.EntityFrameworkCore.MySql` (preferido) vs `MySql.EntityFrameworkCore` — decidir no scaffold conforme compatibilidade de versão do .NET/EF.
- Formato do payload de sinais no `POST /sinistros` (contrato JSON) — definir o mínimo necessário para o fluxo; contrato rico é da fatia 1.
- Estrutura da tabela de "fila humana": coluna de rota no caso vs tabela separada — começar com coluna de rota (normal/reforçada) no caso é suficiente para a fundação.
