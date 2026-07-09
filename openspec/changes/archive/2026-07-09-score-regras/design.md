## Context

A fundação roda ponta a ponta (`API → SQS → Worker → MySQL`), mas o `IScoreProvider` é um mock sinalizado — nenhum score real existe. O `MotorDeDecisao` (Core) já orquestra: resolve config ativa → chama o provider → classifica faixa/rota → produz caso + auditoria, sempre com human-in-the-loop e fail-open. A `scoring_config` já vive versionada no MySQL.

Esta change implementa a Feature 2.3: o motor determinístico real atrás da mesma porta. Restrições que moldam o design:

- **Guardrails inegociáveis**: nunca nega/aprova/bloqueia; auditoria imutável (trigger no MySQL); config governada e versionada; fail-open; não-discriminação.
- **Arquitetura**: `Core` não referencia EF/SQS; adapters e composição na borda.
- **PRD é a autoridade** de decisão; onde ele cala ou se contradiz, o critério é segurança e aderência aos guardrails (decidido em sessão de grilling).
- **A Feature 2.2 (coleta de sinais) é desenvolvida por outro dev**; o valor de `velocity` chega pronto quando ela for mergeada. O contrato de sinais (`Sinal(Nome, Valor, Origem)`) não pode ser quebrado.

## Goals / Non-Goals

**Goals:**
- Score determinístico e reprodutível 0–100 a partir dos 3 sinais booleanos, com pesos/limiares vindos da config versionada.
- Renormalização segura por cobertura parcial, com piso mínimo que impede que 1 sinal dirija uma faixa confiante.
- Garantir por construção (whitelist fechado) + explicitamente (filtro nomeado) que atributo proibido nunca entra no cálculo.
- Auditar cobertura parcial e eventos de conformidade.
- Manter `Core` puro: o motor é função `(sinais, config) → ResultadoScore`, sem I/O.

**Non-Goals:**
- Roteamento/fila (Feature 2.5), coleta real de sinais (2.2), UI de painel.
- Cálculo do velocity a partir de histórico (é produzido pela 2.2).
- Modelo de ML paralelo e detecção de divergência (roadmap).
- Endpoint de agregação para o painel de viés (RF11 satisfeito pela trilha de auditoria; painel fora de escopo).

## Decisions

### D1 — Sinais booleanos, score = soma dos pesos dos verdadeiros
PRD §7 é explícito ("3 sinais booleanos"). O motor lê o `Sinal.Valor` (double no wire) como ativo/inativo (`Valor != 0`), sem mudar o record. **Alternativa rejeitada:** score graduado `Σ(peso×valor)` — mais expressivo, mas o PRD não pede e complica auditoria/explicabilidade.

### D2 — Conjunto fechado de 3 sinais + tri-estado interno
O motor define o conjunto esperado `{reuso_imagem, imei_serie, velocity}` e mapeia a lista recebida para um estado explícito por sinal: `Presente(true) | Presente(false) | Ausente` (ausente = nome esperado não veio na lista). **Alternativa rejeitada:** inferir ausência só por `lista.Count` — não distingue "não coletado" de "coletado e falso", e trataria um bug da 2.2 (sinal derrubado) como cobertura parcial legítima, mascarando falha de qualidade de dado (risco PRD §18).

### D3 — Piso de cobertura ≥2 para renormalizar
- 3 presentes → cálculo cheio.
- exatamente 2 presentes → renormaliza pesos dos presentes para somar 100, marca `CoberturaParcial`, pontua.
- 0 ou 1 presente → **não avaliado** (fail-open), sem score.
Rationale: renormalizar 1 sinal para peso 100 produz faixa de risco confiante (ex.: Alto) sobre 1/3 de cobertura — exatamente o mascaramento do PRD §18. O PRD §15 só trata o extremo "todos ausentes"; o piso ≥2 estende esse princípio de segurança. **Alternativa rejeitada:** seguir o texto literal (renormaliza com ≥1) — mais fiel à letra, mais exposto ao mascaramento.

### D4 — Velocity chega pronto; o motor é função pura
A 2.3 consome `velocity` como booleano produzido pela 2.2; **não** consulta histórico de sinistros. Preserva determinismo (RF10), testabilidade e a regra "Core não referencia infra". **Alternativa rejeitada:** o motor computar velocity via porta de histórico — mete I/O no Core e fura a pureza; além disso o PRD §11/§20 põem a produção de sinal na 2.2. Consequência: a única "indisponibilidade" real é resolver a `scoring_config` (já tratada com fail-open no `MotorDeDecisao`); um motor puro não fica "indisponível".

### D5 — Porta enriquecida: `ResultadoScore` (BREAKING)
`IScoreProvider.CalcularScoreAsync` passa a devolver `ResultadoScore(int? Score, bool CoberturaParcial, IReadOnlyList<string> SinaisUsados, IReadOnlyList<string> SinaisAusentes, string? MotivoNaoAvaliado)`. A lógica de renormalização/piso/cobertura é scoring — mora no motor, não no orquestrador. `MotorDeDecisao` **remove** o pré-check `SinaisIncompletos`, chama o motor e mapeia `Score is null → FailOpen` (`PendenteRevisaoManual`, `Reforcada`), setando `CoberturaParcial` quando presente. **Alternativa rejeitada:** manter `Task<int>` e pôr a lógica de cobertura no `MotorDeDecisao` — espalha o scoring entre orquestrador e provider.

### D6 — Filtro de atributos proibidos explícito + whitelist por construção
Componente `FiltroAtributosProibidos` (Core, puro) com blocklist nomeada do PRD §6 (raça/cor, gênero, orientação sexual, religião, deficiência, idade), **antes** do motor, como desenhado no PRD. Reforçado pelo whitelist fechado dos 3 sinais dentro do motor (nega por padrão — mais forte que blocklist). Sinal filtrado → registrado como evento de conformidade na auditoria (alimenta KPI "taxa de sinais filtrados"). **Alternativa rejeitada:** só whitelist implícito — mais seguro, mas não materializa a caixa que o PRD desenha nem o evento de conformidade nomeado.

### D7 — Config v2 nova e ativa; v1 inativa
Migration publica v2 (`reuso_imagem`=50, `imei_serie`=30, `velocity`=20; `limiar_medio`=30, `limiar_alto`=71) e desativa a v1 placeholder (cujos valores divergem do PRD em nome do 3º sinal, soma dos pesos e limiar alto). Honra BR7 (toda mudança gera nova versão) e a imutabilidade da governança. **Alternativa rejeitada:** reescrever v1 in-place — mais simples, mas fura o versionamento que o próprio PRD exige.

### D8 — Faixa por limiar literal do PRD
"baixo `<30` / médio `30–70` / alto `>70`". Com o código `score >= limiar`, `limiar_medio=30` e `limiar_alto=71` reproduzem o literal (70 = médio, 71 = alto). Sem zona morta (scores são int). `Classificador` inalterado na lógica.

### D9 — Arredondamento fixado para determinismo
A renormalização (sempre sobre 2 sinais no piso) pode gerar peso fracionário (ex.: 62,5). O score final é `Math.Round(Σ pesos renormalizados dos presentes-e-verdadeiros, MidpointRounding.AwayFromZero)`, clampado a [0,100]. Uma convenção única e fixa garante RF10 (mesma entrada + versão → mesmo resultado).

### D10 — Motor real no Core, mock permanece em Infra
`MotorDeRegras` (impl pura de `IScoreProvider`) vive em `Core/Decisao` — é lógica de domínio sem dependência de infra. O mock sinalizado permanece em `Infra`, atualizado ao novo retorno, usado em testes/fallback. DI (`AddAntifraudeInfra`) passa a registrar o `MotorDeRegras` como `IScoreProvider` no caminho real. **Alternativa considerada:** pôr o motor em Infra por consistência com "providers em Infra" — rejeitada porque o motor não tem infra e o guardrail é "Core = domínio puro".

## Risks / Trade-offs

- **Renormalização mascara cobertura baixa** → piso ≥2 (D3) + marcação obrigatória `CoberturaParcial` + KPI de taxa de cobertura parcial.
- **Contrato de `velocity` com a 2.2 (outro dev)** → o nome canônico `velocity` e a semântica booleana são fixados aqui e documentados na spec `risk-score-engine`; risco de desalinhamento se a 2.2 usar outro nome. Mitigação: constante compartilhada de nomes de sinal no Core + cenário de spec que fixa o contrato.
- **Mudança BREAKING na porta** (D5) → todos os implementadores (`MotorDeRegras`, mock) e o `MotorDeDecisao` mudam no mesmo commit; a assinatura antiga não fica meio-migrada.
- **v1 vs v2 na config** → casos antigos mantêm a versão que os originou (requisito já existente do `scoring-config-store`); ativar v2 não reescreve histórico.
- **ALTER TABLE na `auditoria` protegida por trigger** → o trigger bloqueia UPDATE/DELETE (DML), não DDL; adicionar coluna `cobertura_parcial` via migration é permitido.

## Migration Plan

1. Migration EF: adiciona `cobertura_parcial` (bool, default false) em `casos` e `auditoria`; insere `scoring_config` v2 e seta `ativa=0` na v1, `ativa=1` na v2.
2. Deploy: migrations aplicadas no start da API (antes de ficar healthy), como já é o fluxo.
3. Rollback: nova migration inversa remove as colunas e reativa a v1; nenhum caso é apagado (auditoria imutável). Como v2 é aditiva, rollback só reverte a config ativa e o schema.

## Open Questions

- Nenhuma bloqueante. O nome canônico do 2º sinal foi encurtado de `imei_serie_divergente` (seed placeholder) para `imei_serie` (PRD); confirmar alinhamento com a 2.2 quando ela abrir PR.
