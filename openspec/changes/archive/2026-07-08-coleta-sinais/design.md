# Design — coleta-sinais (Feature 2.2)

## Context

O Worker consome a fila, roda o `MotorDeDecisao` (config ativa → score → faixa/rota) e
persiste caso + auditoria. Hoje o `Sinistro` chega com `Sinais` vazio e o motor aplica
fail-open (`PendenteRevisaoManual`, causa "Sinais faltantes ou parciais"). O record
`Sinal(Nome, Valor double, Origem)` não comporta o tri-estado exigido pelo PRD 2.2
(ativo / inativo / **indisponível**) nem evidência auditável. A `scoring_config` v1
semeada tem pesos para `reuso_imagem` e `imei_serie_divergente`, mas não para `velocity`.

## Goals / Non-Goals

**Goals:**

- Calcular os 3 sinais fixos (reuso de imagem, IMEI×série×apólice, velocity) de forma
  independente e paralela, dentro do Worker, antes do `MotorDeDecisao`.
- Tri-estado explícito com motivo de indisponibilidade (dado ausente × fonte fora) e
  evidência específica por sinal, registrada imutavelmente na auditoria.
- Resiliência por fonte: timeout + circuit breaker independentes; queda de uma fonte
  afeta só o sinal correspondente.
- Fontes de dados como fakes locais **sinalizados** (mesmo padrão do `MockScoreProvider`),
  com indisponibilidade simulável para demo/testes.

**Non-Goals:**

- Score, renormalização de pesos e faixa de risco (feature 2.3) — o `MockScoreProvider`
  continua sendo o placeholder sinalizado.
- Roteamento por fila (2.5); demais sinais do catálogo (EXIF, edição, geolocalização,
  aparelho indenizado); download/processamento real de bytes de imagem.
- Mudança no contrato da API (`POST /sinistros`).

## Decisions

### D1 — Coleta roda dentro do Worker existente, como passo antes do motor

O `Worker.ProcessarAsync` passa a chamar `ColetorDeSinais.ColetarAsync(sinistro)` e segue
para o `MotorDeDecisao` com o `Sinistro` enriquecido (`sinistro with { Sinais = ... }`).
*Alternativa rejeitada:* novo serviço/fila intermediária entre 2.1 e 2.3 — complexidade de
infra sem exigência do PRD; a fronteira lógica fica no `ColetorDeSinais` (Core), que pode
ser extraído para um serviço próprio no futuro sem mudar o domínio.

### D2 — `Sinal` evolui para tri-estado + evidência (sem tipo paralelo)

O record `Sinal` ganha: `Estado` (`ValorSinal`: `Ativo | Inativo | Indisponivel`),
`Evidencia` (texto estruturado curto, específico por sinal), `Motivo`
(`MotivoIndisponibilidade?`: `DadoAusente | FonteIndisponivel`) e `CalculadoEm`. O campo
`Valor` (double) sai; o `MockScoreProvider` mapeia `Ativo→1.0`, `Inativo→0.0` e **ignora**
`Indisponivel` (a renormalização correta é da 2.3). `Sinistro.SinaisIncompletos` passa a
ser: lista vazia **ou** todos os sinais indisponíveis — preservando o fail-open atual
("não avaliado") quando as 3 fontes falham. **Indisponibilidade parcial** (1–2 sinais):
o caso segue o fluxo normal de score com os sinais disponíveis (fiel ao PRD 2.2 e à
renormalização da 2.3), estado `RoteadoParaRevisao`, mas com `DadosIncompletos = true`
visível ao analista e os motivos por sinal na auditoria.
*Alternativa rejeitada:* criar `SinalColetado` separado convivendo com `Sinal` — dois
tipos para o mesmo conceito confundem a 2.3 e a auditoria.

### D3 — Uma porta por fonte de dados, cálculo do sinal no Core

Portas novas em `Core/Portas` (dados crus na Infra, regra no Core):

- `IRepositorioDeImagens` — devolve o pHash (64 bits) de cada foto do sinistro e os
  hashes do histórico dos últimos 6 meses; registra os hashes do sinistro atual. A
  **distância de Hamming (≤ 10)** é calculada no Core (`CalculadorReusoImagem`).
- `IBaseDeApolices` — devolve o aparelho cadastrado (IMEI/série) da apólice, ou "não
  cadastrado". A comparação divergente × não cadastrado é regra do Core.
- `IHistoricoDeSinistros` — devolve contagens de sinistros por cliente e por IMEI na
  janela de 90 dias; registra o sinistro atual no histórico. O limiar (≥2) é regra do
  Core. A janela conta a partir de `abertoEm` quando presente, senão da data de
  processamento (a evidência registra qual referência temporal foi usada).

**Invariantes do histórico** (imagens e sinistros): (1) toda consulta exclui o próprio
`idSinistro` — o caso nunca colide nem se conta consigo mesmo, inclusive em
reprocessamento de mensagem SQS; (2) o registro do sinistro atual acontece **depois** do
cálculo e é **upsert idempotente** por `idSinistro` — retentativas não duplicam linhas
nem inflam contagens.

Cada calculador implementa `ICalculadorDeSinal` (`Nome` + `CalcularAsync(Sinistro)`), e:
(1) verifica dado de entrada no payload → `Indisponivel(DadoAusente)` sem tocar a fonte;
(2) captura exceção/timeout da fonte → `Indisponivel(FonteIndisponivel)`;
(3) senão produz `Ativo`/`Inativo` com evidência. O `ColetorDeSinais` roda os 3 com
`Task.WhenAll` — nenhuma exceção escapa de um calculador.

### D4 — Fakes locais persistidos no MySQL, sinalizados

- **Imagens**: tabela `imagem_hashes` (`case_id`, `id_sinistro`, `foto_ref`, `phash`
  BIGINT, `criado_em`, índice em `criado_em`). Sem bytes de imagem nesta fatia, o adapter
  deriva o pHash de forma **determinística a partir da referência da foto** (origem
  carimbada `phash-fake-v1`) — mesma referência ⇒ mesmo hash ⇒ distância 0 ⇒ reuso
  detectável na demo. Janela de 6 meses aplicada na consulta.
- **Apólices**: tabela `apolices` (`apolice`, `imei`, `numero_serie`) com seed de
  exemplos no `DbSeeder` cobrindo os 3 ramos (confere, diverge, não cadastrado).
- **Histórico**: tabela `historico_sinistros` (`id_sinistro`, `id_cliente`, `imei`,
  `aberto_em`, índices por `id_cliente` e `imei`), alimentada na própria coleta.
- Cada adapter expõe `SimularIndisponibilidade` via env var
  (`FONTE_IMAGENS_INDISPONIVEL`, `FONTE_APOLICES_INDISPONIVEL`,
  `FONTE_HISTORICO_INDISPONIVEL`) — prova os cenários de exceção do PRD em demo/teste.

*Alternativa rejeitada:* fakes em memória — não sobrevivem a restart e não permitem
histórico entre casos (essencial para reuso de imagem e velocity).

### D5 — Resiliência: timeout via `CancellationTokenSource` + circuit breaker leve por fonte

Decorator `FonteResiliente` na Infra em volta de cada adapter: timeout curto configurável
(default 5s, env var) e circuit breaker simples (N falhas consecutivas abrem o circuito
por T segundos; default 3 falhas / 30s). *Alternativa considerada:* Polly v8 — fica
natural quando as fontes virarem integrações HTTP reais; para adapters MySQL locais, a
dependência não paga o custo agora. A porta não muda quando essa troca acontecer.

### D5b — Contexto EF próprio por operação de fonte (descoberto na implementação)

Os calculadores rodam em paralelo (`Task.WhenAll`) e o `DbContext` do EF **não é
thread-safe**: com os adapters compartilhando o contexto scoped, a exceção de
concorrência era capturada pelos calculadores e virava `FonteIndisponivel` silencioso
(descoberto pelos testes de integração). Os adapters de fonte usam
`IDbContextFactory<AntifraudeDbContext>` — um contexto curto por operação — mantendo o
`DbContext` scoped para o resto (motor, repositórios, auditoria).

### D6 — Nomes canônicos e config de scoring

Nomes dos sinais: `reuso_imagem`, `imei_serie_divergente` (chaves já existentes na
`scoring_config` v1) e `velocity`. **Não** alteramos a config governada nesta change —
o peso de `velocity` (e a saída de `geolocalizacao_inconsistente`) é decisão de
calibração da 2.3/PO. Consequência aceita: no mock atual, `velocity` não contribui para
o score até a config ganhar o peso — mas o sinal já nasce calculado, auditado e visível.

### D7 — Auditoria e LGPD

`RegistroAuditoria.Sinais` já serializa para `auditoria.sinais_json` (tabela imutável por
trigger) — passa a carregar estado + evidência + motivo + origem por sinal. A evidência é
um **objeto JSON pequeno e estruturado por sinal** (renderizável pelo Console/painel 2.7 e
consultável pelo Compliance): reuso → `{sinistroColidido, distancia, foto}` (a **melhor
colisão por foto** — menor distância; não todas); IMEI×série →
`{motivo: diverge|nao_cadastrado, informado, cadastrado}`; velocity →
`{contagem, janelaDias, criterio, referenciaTemporal}`. Evidências mascaram
identificadores: IMEI/série truncados aos **últimos 4 dígitos** (ex.: `…3809`), fotos
sempre por referência, nunca payload bruto. Observabilidade via logs estruturados por sinal
(estado, motivo, latência da fonte), correlacionados por `caseId`.

## Risks / Trade-offs

- [pHash derivado da referência, não da imagem] → detecta reuso literal da mesma
  referência, não similaridade visual real. Mitigação: origem `phash-fake-v1` carimbada
  em toda evidência (padrão "mock sinalizado"); a troca por pHash real é isolada no
  adapter.
- [Velocity sem peso na config v1 ⇒ não move o score do mock] → sinal fica "invisível"
  no score até a 2.3 calibrar. Mitigação: decisão explícita (D6), sinal auditado e
  logado desde já; PO decide a config v2 na 2.3.
- [Coleta adiciona latência ao worker (3 fontes + timeouts)] → pior caso ≈ timeout da
  fonte mais lenta (paralelo), dentro do SLA de 5 min. Mitigação: timeout default 5s por
  fonte e circuit breaker corta fontes cronicamente fora.
- [Tabela `historico_sinistros` alimentada na coleta pode duplicar em reprocessamento
  de mensagem] → upsert por `id_sinistro` (idempotente, alinhado à dedup da 2.1).
- [Mudar o record `Sinal` quebra testes/mock existentes] → mudança contida em Core +
  MockScoreProvider + testes; nenhuma tabela existente muda de schema (sinais_json é
  JSON livre).

## Open Questions

Nenhuma — as questões abertas originais foram resolvidas em sessão de grill (2026-07-08)
e incorporadas às decisões acima:

- Janela do velocity: `abertoEm` com fallback para data de processamento (D3).
- Registro do sinistro atual: depois do cálculo, com as 2 invariantes — consulta exclui o
  próprio `idSinistro` e upsert idempotente (D3).
- Evidência de reuso: melhor colisão (menor distância) por foto (D7).
- Indisponibilidade parcial: caso segue para score com os sinais disponíveis +
  `DadosIncompletos = true`; fail-open total só com 3/3 indisponíveis (D2).
- `scoring_config` intocada nesta change; peso de `velocity` é calibração da 2.3/PO (D6).
