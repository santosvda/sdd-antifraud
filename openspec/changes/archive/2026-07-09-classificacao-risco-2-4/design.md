## Context

A fundação já classifica: `Classificador.FaixaPara` (score → faixa, intervalo fechado-aberto, limiares da `ScoringConfig` versionada), `Classificador.RotaPara` (faixa → rota), `Faixa.Indeterminado` para fail-open, e o carimbo de `VersaoConfig`/`VersaoProvider` no `Caso` e no `RegistroAuditoria`. Faltam três coisas para a Feature 2.4: (a) o **gerador de explicação textual** ao analista, (b) a correção do **clamp silencioso** do score (`MotorDeDecisao.cs:60` faz `Math.Clamp(score, 0, 100)`), e (c) a **distinção tipada** entre os casos de "sem classificação".

**Restrição dominante:** as features 2.2 (coleta de sinais) e 2.3 (score & regras) estão sendo implementadas em paralelo por outros devs. Esta change **não pode alterar** o contrato de score (`IScoreProvider`, `Sinal`, `Sinistro`, `ScoringConfig`) — colidiria com o trabalho deles. Toda a mudança é aditiva; o único ponto de contato é um seam mínimo no `MotorDeDecisao`, combinado no merge.

**Reachability atual:** no pipeline vivo todo caso cai em fail-open (sinais não fluem até a 2.2; `Sinistro.SinaisIncompletos` curto-circuita antes do score; o `MockScoreProvider` também clampa). Os caminhos novos (faixa real, nomear sinais, cobertura parcial, anomalia out-of-range) só são exercitáveis por unit test e por test-double.

## Goals / Non-Goals

**Goals:**
- Gerar explicação textual determinística, não-acusatória, versionada, associada à faixa.
- Corrigir o clamp: score fora de [0,100] vira anomalia (sem classificação + alerta técnico severidade alta), não valor coagido.
- Tipar os casos sem classificação (`MotivoSemClassificacao`) para distinguir indisponibilidade esperada de anomalia — auditável e governando o disparo do alerta.
- Persistir explicação, versão de template e motivo no caso e na trilha imutável.
- Manter tudo aditivo, sem tocar o contrato de score de 2.2/2.3.

**Non-Goals:**
- Calcular o score (2.3), decidir a fila (2.5), montar o alerta completo por sinal (2.6).
- Construir o canal de alerta real (plantão/PagerDuty) — só a porta + adapter de log.
- Resolver "última versão válida conhecida" de config corrompida — resiliência é da camada de config (2.3).
- Geração de linguagem natural livre (LLM) — sempre template determinístico nesta fase.
- Alterar `RotaPara`, `IScoreProvider`, `Sinal`, `Sinistro`, `ScoringConfig`.

## Decisions

**1. Placement — estender o Core, sem novo serviço.** Novo `GeradorDeExplicacao` puro no `Core`; a explicação entra no `ResultadoDecisao`. *Por quê:* fiel ao "ports & adapters leve" (Core sem infra) e ao menor atrito. *Alternativas:* serviço/processo separado (adiciona hop e infra que o walking skeleton não tem); módulo isolado com contrato próprio (peso desnecessário enquanto 2.3 não é serviço separado).

**2. Score fora de [0,100] — remover o clamp, tratar como anomalia.** No seam, em vez de `Math.Clamp`, detectar out-of-range → sem classificação (revisão manual) + alerta severidade alta. *Por quê:* RF09; o clamp mascara bug upstream. *Alternativas:* manter clamp + alertar (viola RF09, que exige "sem classificação"); validar só no provider (é território 2.3 e RF09 exige a 2.4 se defender de erro upstream).

**3. Canal de alerta — porta `IAlertaTecnico` no Core + adapter de log Critical.** Método tipo `EmitirAsync(severidade, codigo, contexto)`; adapter na Infra loga estruturado nível Critical com `caseId`. *Por quê:* fiel a ports&adapters, deixa gancho para plantão sem construí-lo. *Alternativas:* log direto no Core (mistura observabilidade no domínio, sem contrato); auditoria como alerta (§19 pede canal distinto do operacional).

**4. Distinguir "sem classificação" — enum `MotivoSemClassificacao`.** Valores: `SinalAusente`, `ProviderIndisponivel` (indisponibilidade esperada — não alerta) e `ConfigIndisponivel`, `ConfigCorrompida`, `ScoreForaDeFaixa` (anomalias — alertam; §15: nunca operar sem limiares validados). Carimbado no `Caso` e na `Auditoria`, ao lado da `Causa` textual. O disparo do alerta é decidido pelo motivo, não por parse de string. *Por quê:* RF10 + auditabilidade. *Alternativas:* flag booleana `AnomaliaTecnica` (não separa tipos); novo `EstadoDoCaso` (mistura estado com motivo e muda contrato de roteamento — a rota é a mesma).

**5. Persistir a explicação — `Caso` e `RegistroAuditoria`.** Migration aditiva. *Por quê:* §5 (analista lê do caso) + §13 (texto no registro imutável); consistente com Faixa/Rota/Score já duplicados. *Alternativas:* só auditoria (força join para exibir rotina); só caso + regenerar (viola §13, quebra se o template mudar de versão).

**6. Template — em código, versionado por `VersaoTemplate`.** Constante determinística no Core; `VersaoTemplate` carimbada junto da versão de limiares; compliance revisa por PR/diff. *Por quê:* determinismo + auditabilidade sem schema novo agora. *Alternativas:* config versionada em tabela (peso desnecessário antes de haver sinais reais); dentro da `scoring_config` (acopla governança de limiar (antifraude) com texto (compliance) — donos e cadências diferentes).

**7. Nomes de exibição dos sinais — mapa em código, com fallback seguro.** Dicionário `id-técnico → nome-exibição` versionado junto do template; sinal desconhecido cai em fallback (ex.: "outro indicador"), nunca vaza o id cru. *Alternativas:* config versionada (divide a fonte do texto em dois lugares); `NomeExibicao` no `Sinal` (mexe no contrato de 2.2).

**8. "Sinal ativado" = `Valor > 0`.** Nomeia só sinais que contribuíram. *Alternativas:* limiar de relevância (introduz número governado que a PRD não menciona); todos os sinais (explicação longa, cita indícios que não pesaram — arriscado no tom).

**9. Texto em fail-open (RF06) — explicação `null` + rótulo canônico do motivo.** Sem faixa/texto de faixa inventado. A "marca" é o `MotivoSemClassificacao`, que expõe um rótulo canônico curto e não-acusatório (ex.: "Não avaliado por IA — revisão manual"), produzido num único lugar no Core. *Por quê:* honra RF06 e mantém consistência entre consumidores (§2). *Alternativas:* gerar uma "explicação de fail-open" (borra a fronteira classificado/sem-classificação); nada além do enum (espalha a redação por cada consumidor).

**10. Cobertura parcial (RF05) — parâmetro próprio do gerador.** `GeradorDeExplicacao` recebe `coberturaParcial` como entrada; o seam passa `false` até a 2.3 expor o dado. Sem tocar `IScoreProvider`. O galho `true` é coberto por unit test. *Por quê:* cumpre RF05 sem invadir o contrato de score. *Alternativas:* enriquecer o retorno do score agora (muda `IScoreProvider`, colide com 2.3); reusar `DadosIncompletos` (é o oposto — significa fail-open sem score).

**11. Integração — aditivo + um seam mínimo.** Só arquivos novos, exceto um ponto de inserção pequeno no `MotorDeDecisao` (remover clamp, delegar out-of-range à classificação, invocar o gerador, carimbar motivo). Ponto de merge combinado com o dev da 2.3. *Alternativas:* standalone sem fiar no pipeline (não demonstrável e2e); seam de contrato acordado (`IClassificadorDeRisco`) — mais limpo a longo prazo mas trava a 2.4 até acordo com os outros devs.

**12. Verificação — unit + integração com test-double próprio.** Unit do gerador e do classificador com entradas sintéticas; integração com um `IScoreProvider` de teste no projeto Tests que injeta sinais e scores fora de faixa. Não toca o `MockScoreProvider` compartilhado. *Alternativas:* só unit + e2e no fail-open (recursos-cabeça nunca rodam no pipeline montado); knobs no `MockScoreProvider` (colide com 2.3).

**13. Config corrompida (§15) — 2.4 só marca motivo + alerta.** A resiliência "última versão válida conhecida" fica com a camada de config (2.3). *Por quê:* evita duplicar governança de config e invadir 2.3.

**Derivados (sem conflito):** `RotaPara` fica como está — anomalia e fail-open → `Faixa.Indeterminado` → `Rota.Reforcada` (revisão manual); o `MotivoSemClassificacao` é que distingue. Rótulos canônicos (dec. 9) e nomes de exibição (dec. 7) moram no mesmo módulo de template, sob a mesma `VersaoTemplate`.

## Risks / Trade-offs

- **[Merge conflict no `MotorDeDecisao` com a 2.3, que edita o mesmo arquivo]** → Manter o seam mínimo e localizado (idealmente extrair a lógica nova para tipos 2.4 e deixar no motor só a chamada); combinar o ponto de inserção com o dev da 2.3 antes de abrir o PR.
- **[Explicação por template soa genérica]** → Versionar o template (`VersaoTemplate`) para iterar com feedback de analistas e comparar efetividade; revisão de compliance sobre o texto.
- **[Nomear sinais parecer acusatório se vazar do backoffice]** → Linguagem de indício obrigatória; texto destinado só ao painel do analista; revisão de compliance dos templates.
- **[Caminhos novos não exercitáveis e2e enquanto 2.2/2.3 não assentam]** → Cobertura por unit + test-double (dec. 12); e2e vivo continua validando o fail-open (o único caminho alcançável hoje).
- **[Migration aditiva x trigger de imutabilidade da auditoria]** → DDL aditivo (colunas nullable) é compatível: INSERT continua permitido, UPDATE/DELETE continuam bloqueados. Validar que a migration não recria o trigger de forma a permitir mutação.

## Migration Plan

1. Migration EF aditiva: colunas `Explicacao` (texto, nullable), `VersaoTemplate` (nullable), `MotivoSemClassificacao` (nullable) em `casos` e `auditoria`. Aplicada no start da API (`Database.Migrate()`), como as demais.
2. Nullable + backfill implícito: casos antigos ficam com as colunas nulas — aceitável (não há reprocessamento). Rollback = migration `Down` que dropa as colunas; sem perda de dados de negócio existentes.
3. Sem seed novo (template vive em código). Sem mudança na `scoring_config`.

## Open Questions

- Formato exato do rótulo canônico por `MotivoSemClassificacao` — texto a validar com compliance (não bloqueia a estrutura).
- Ponto exato do seam no `MotorDeDecisao` a alinhar com o dev da 2.3 (arquivo compartilhado).
