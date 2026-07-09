# PRD — Motor Antifraude de Sinistros
## Feature 2.4: Classificação de Risco
**ACME Seguros — Seguro de Celular — Trilha B (sinistro por foto)**

---

## 1. Visão Geral

Esta feature recebe o **score numérico (0–100)** calculado pela feature 2.3 e o traduz em algo que o analista consegue interpretar diretamente: uma **faixa de risco** (baixo/médio/alto) e uma **explicação textual, curta e não acusatória**, de por que aquele caso caiu naquela faixa. É a camada de "tradução" entre o número que o motor calcula e o texto que o ser humano lê — sem essa feature, o analista veria só um número, sem contexto de por que ele importa.

Esta feature **não** calcula o score (feature 2.3), **não** decide para qual fila o caso vai (feature 2.5) e **não** monta o alerta completo com evidência detalhada de cada sinal (feature 2.6) — ela apenas classifica e gera a explicação de nível "faixa", que serve de insumo para as duas.

> **Nota de fronteira com a feature 2.3:** a lógica de limiares de faixa foi descrita, por pragmatismo, dentro da arquitetura da feature 2.3 (como um passo interno de "Classificador de Faixa"). Esta PRD formaliza a **Classificação de Risco como contrato próprio e explícito**: a feature 2.3 deve produzir o score numérico e delegar a esta feature a aplicação dos limiares e a geração da explicação — o comportamento observável não muda, mas a responsabilidade fica claramente atribuída, o que importa se as duas forem implementadas como componentes/serviços separados.

## 2. Problema

Um score numérico isolado (ex.: "72") não diz nada por si só ao analista sobre o que fazer com o caso, nem por que aquele valor foi atingido. Sem uma camada dedicada de classificação e explicação, cada consumidor do score (fila, painel, relatório) teria que reimplementar a lógica de interpretação — com risco real de linguagem acusatória, inconsistência entre faixas exibidas em lugares diferentes, e falta de rastreabilidade de qual versão de limiar gerou qual classificação.

## 3. Objetivos

- Classificar o score numérico em uma faixa de risco (baixo/médio/alto), usando os limiares vigentes e configuráveis (herdados da governança já definida na feature 2.3: baixo <30, médio 30–70, alto >70).
- Gerar uma explicação textual curta, legível e não acusatória, associada à faixa.
- Propagar corretamente os casos de cobertura parcial (sinal ausente) e de indisponibilidade total do score (fail-open), sem forçar uma classificação onde não há score.
- Garantir que a classificação seja determinística e auditável: mesma entrada + mesma versão de limiares → mesma faixa e mesma explicação.

**Não-objetivos desta feature:** calcular o score, decidir a fila de roteamento, montar o alerta completo com evidência por sinal, sugerir prioridade de análise (isso é a feature 2.6).

## 4. Personas

| Persona | Papel nesta feature |
|---|---|
| **Feature 2.3 (Score & Regras)** | Produtora da entrada desta feature (score, sinais usados, versão de configuração, cobertura parcial). |
| **Feature 2.5 (Roteamento)** | Consumidora da faixa de risco, para decidir a fila. |
| **Feature 2.6 (Geração de Alertas)** | Consumidora da explicação textual de faixa, como base para o alerta completo. |
| **Analista de sinistro** | Beneficiário final — lê a faixa e a explicação já traduzidas. |
| **Equipe antifraude** | Dona da calibração dos limiares (mesma governança já estabelecida na feature 2.3). |

## 5. Jornada (Analista, indireta)

1. O score chega da feature 2.3, com seus metadados (sinais usados, versão de configuração, cobertura parcial se houver).
2. Esta feature aplica os limiares vigentes e atribui a faixa.
3. Esta feature gera uma frase curta e não acusatória explicando a faixa (ex.: "Score de risco: 72/100 — faixa alta. Dois indícios foram identificados neste sinistro.").
4. O resultado (faixa + explicação + metadados herdados) segue para a feature 2.5 (roteamento) e, mais tarde, alimenta a feature 2.6 (alerta completo).
5. O analista, ao abrir o caso, lê a faixa e a explicação já prontas — nunca um número solto sem contexto.

## 6. Fluxo Completo (com caminho de score ausente)

```
[Score produzido pela Feature 2.3]
        │
        ▼
┌─────────────────────────────┐
│ Score foi calculado           │
│ (não é caso de fail-open)?    │
└───────┬───────────────┬──────┘
    sim │               │ não (fail-open da 2.3:
        ▼               │ "não avaliado por IA")
┌─────────────────────────────┐   │
│ Classificador de Faixa         │   ▼
│ (limiares vigentes: baixo <30,│  Repassa direto para a
│ médio 30–70, alto >70)        │  Feature 2.5 sem faixa —
└───────────────┬────────────────┘  marca "sem classificação
                ▼                    (fail-open upstream)"
┌─────────────────────────────┐
│ Gerador de Explicação Textual  │
│ (template determinístico,      │
│ não acusatório, menciona       │
│ score, faixa e cobertura       │
│ parcial se houver)             │
└───────────────┬────────────────┘
                ▼
┌─────────────────────────────┐
│ Registro de Auditoria          │
│ (faixa, explicação, versão de  │
│ limiares usada)                │
└───────────────┬────────────────┘
                ▼
   Saída para Feature 2.5 (Roteamento)
   e Feature 2.6 (Geração de Alertas)
```

**Ponto crítico do guardrail:** se a feature 2.3 sinalizou "não avaliado" (fail-open), esta feature **não inventa uma faixa** — apenas repassa a marca de "sem classificação" adiante, para a feature 2.5 tratar como fila de revisão manual.

## 7. Regras de Negócio

1. A faixa é atribuída conforme os limiares vigentes e configuráveis, usando **intervalo fechado-aberto**: **baixo = [0, 30), médio = [30, 70), alto = [70, 100]** (mesma governança de calibração já definida na feature 2.3 — equipe antifraude, revisão trimestral + gatilho reativo, mudança versionada e auditável).
2. Se a feature 2.3 sinalizar que não houve score calculado (fail-open), esta feature não classifica — repassa a marca de "sem classificação" para a feature 2.5, sem inventar uma faixa padrão.
3. A explicação textual é gerada por **template determinístico definido em código** (não por geração livre de linguagem natural), garantindo reprodutibilidade e conformidade automática com a exigência de "sem linguagem acusatória" — a mesma entrada sempre produz a mesma frase. O template é versionado (`VersaoTemplate`) e revisado por compliance via PR/diff (ver §23).
4. A explicação sempre menciona: o valor do score, a faixa atribuída, **os nomes de exibição dos sinais ativados** (em linguagem de indício, nunca de conclusão — ex.: "indícios de reuso de imagem", nunca "reuso de imagem confirmado"), e, se aplicável, que o cálculo teve cobertura parcial (sinal ausente). Um sinal é considerado **ativado** quando está presente com `Valor > 0` (contribuiu de fato ao score); os nomes de exibição vêm de um mapa em código versionado junto do template, com fallback seguro para sinal desconhecido (nunca vaza o identificador técnico cru).
5. A explicação nunca usa linguagem que afirme fraude como fato consumado (ex.: "fraude confirmada", "cliente mentiu") — sempre linguagem de indício/hipótese (ex.: "foram identificados N indícios", "o score sugere atenção").
6. Toda classificação registra a **versão dos limiares** (`VersaoConfig`) **e a versão do template** (`VersaoTemplate`) usadas, para auditoria e para permitir reconstituir por que um caso antigo recebeu determinada faixa e determinado texto mesmo após limiares ou template mudarem.
7. A classificação é determinística: mesmo score + mesma versão de limiares + mesma versão de template → mesma faixa e mesma explicação, sempre.
8. Um score fora do intervalo esperado (ex.: negativo ou >100, por erro upstream) é tratado como **equivalente ao fail-open** para fins de roteamento (sem classificação, sinistro segue para revisão manual), mas gera um **alerta técnico de severidade alta** — distinto do alerta padrão de indisponibilidade esperada, pois indica anomalia no motor de score. Este caso e os demais casos de "sem classificação" são distinguidos por um **motivo tipado** (`MotivoSemClassificacao`), não por texto livre — ver §23. **Nota de implementação:** o pipeline atual (`MotorDeDecisao`) hoje faz `Math.Clamp(score, 0, 100)`, coagindo silenciosamente valores fora de faixa; esta regra **exige a remoção desse clamp** e sua substituição pela detecção de anomalia descrita aqui.

## 8. Arquitetura de Alto Nível

```
┌─────────────────────────┐
│ Feature 2.3                │
│ (Score & Regras)           │
└────────────┬──────────────┘
             │ score (0–100) + sinais usados +
             │ versão de config + cobertura parcial
             │ (ou marca de "não avaliado")
             ▼
┌──────────────────────────────────────┐
│ FEATURE 2.4 — Classificação de Risco     │
│                                          │
│  ┌───────────────────────────┐         │
│  │ Classificador de Faixa        │◀────┼── Configuração de limiares
│  │ (limiares configuráveis)      │      │   (versionada)
│  └─────────────┬─────────────────┘      │
│                ▼                        │
│  ┌───────────────────────────┐         │
│  │ Gerador de Explicação          │      │
│  │ (template determinístico,      │      │
│  │ não acusatório)                │      │
│  └─────────────┬─────────────────┘      │
│                ▼                        │
│  ┌───────────────────────────┐         │
│  │ Publicador de Auditoria        │──────┼──▶ Log imutável
│  └─────────────┬─────────────────┘      │
└────────────────┼─────────────────────────┘
                 ▼
   ┌─────────────────────────┐   ┌─────────────────────────┐
   │ Feature 2.5 (Roteamento)  │   │ Feature 2.6 (Alertas)     │
   └─────────────────────────┘   └─────────────────────────┘
```

## 9. Requisitos Funcionais

| ID | Requisito |
|---|---|
| RF01 | O sistema deve classificar o score recebido em uma das três faixas (baixo/médio/alto), usando intervalo fechado-aberto: baixo [0,30), médio [30,70), alto [70,100]. |
| RF02 | O sistema deve usar limiares configuráveis (herdados da mesma configuração versionada da feature 2.3), nunca hard-coded. |
| RF03 | O sistema deve gerar uma explicação textual associada à faixa, usando template determinístico definido em código e versionado (`VersaoTemplate`). |
| RF04 | A explicação gerada deve nomear os sinais ativados (`Valor > 0`) por seu nome de exibição, em linguagem de indício, e nunca conter linguagem acusatória ou afirmar fraude como fato consumado. |
| RF05 | A explicação deve mencionar explicitamente quando o score foi calculado com cobertura parcial (sinal ausente). O gerador recebe `coberturaParcial` como parâmetro próprio; sem alterar o contrato de score da feature 2.3. |
| RF06 | Quando a feature 2.3 sinalizar "não avaliado" (fail-open), esta feature não deve gerar faixa nem explicação de faixa — apenas repassar a marca adiante (motivo tipado + rótulo canônico). |
| RF07 | O sistema deve registrar a versão dos limiares (`VersaoConfig`) e a versão do template (`VersaoTemplate`) usadas em cada classificação. |
| RF08 | A classificação e a explicação devem ser determinísticas: mesma entrada + mesma versão de limiares + mesma versão de template produzem sempre o mesmo resultado. |
| RF09 | Um score fora do intervalo [0,100] deve ser tratado como equivalente ao fail-open (sem classificação, motivo `ScoreForaDeFaixa`) e gerar alerta técnico de severidade alta. O `Math.Clamp` atual do pipeline deve ser removido. |
| RF10 | Cada caso "sem classificação" deve carimbar um motivo tipado (`MotivoSemClassificacao`) que distinga indisponibilidade esperada de anomalia técnica, no `Caso` e na auditoria. |

## 10. Requisitos Não Funcionais

- **Latência:** classificação e geração de explicação são operações leves (comparação numérica + template); não devem adicionar latência perceptível ao orçamento de SLA (≤5 min p95 total).
- **Determinismo:** requisito formal, testável (RF08).
- **Auditabilidade:** toda classificação gera registro imutável com faixa, explicação e versão de limiares.
- **Consistência:** a mesma faixa/explicação deve ser exibida de forma idêntica em qualquer consumidor (fila, painel, futuros relatórios).

## 11. Integrações

- **Feature 2.3 (Score & Regras)** — fonte do score e seus metadados.
- **Serviço de Configuração** — mesma configuração versionada de limiares usada pela feature 2.3.
- **Feature 2.5 (Roteamento)** — consumidora da faixa (ou da marca de "sem classificação").
- **Feature 2.6 (Geração de Alertas)** — consumidora da explicação textual como base do alerta completo.
- **Log/Auditoria** — armazenamento imutável de cada classificação.

## 12. Segurança e LGPD

- Esta feature não introduz novo tratamento de dados pessoais além do que já chega da feature 2.3 (score e metadados técnicos) — não há dado pessoal adicional processado aqui.
- A explicação textual gerada não deve incluir dados pessoais do cliente (nome, CPF, etc.) — apenas informação sobre o próprio cálculo (score, faixa, sinais por nome genérico).
- Acesso ao registro de classificação segue a mesma segregação analista vs. compliance já definida para o motor.

## 13. Auditoria

Para cada classificação, registrar de forma imutável:
- Score recebido e faixa atribuída (ou marca de "sem classificação" em caso de fail-open upstream).
- Texto da explicação gerada.
- Versão dos limiares usada.
- Indicação de cobertura parcial, se herdada da feature 2.3.
- Timestamp e identificador do sinistro.

## 14. Casos de Uso

1. **Classificação padrão:** score = 72 → faixa "alto", explicação: "Score de risco: 72/100 — faixa alta. Este caso apresenta indícios de reuso de imagem e inconsistência de IMEI×série."
2. **Classificação com cobertura parcial:** score calculado com um sinal ausente (renormalizado pela feature 2.3) → faixa atribuída normalmente, mas explicação menciona explicitamente a cobertura parcial e nomeia apenas os sinais efetivamente avaliados.
3. **Score no limite exato de uma faixa:** score = 30 → classificado como "médio" (intervalo fechado-aberto: médio inclui 30); score = 70 → classificado como "alto" (alto inclui 70).
4. **Fail-open upstream:** feature 2.3 sinaliza "não avaliado" → esta feature não classifica, repassa a marca adiante para a feature 2.5.
5. **Mudança de limiares pela equipe antifraude:** nova versão de limiares publicada → classificações subsequentes usam a nova versão; classificações antigas mantêm o registro da versão histórica usada no momento.

## 15. Casos de Exceção

| Exceção | Tratamento |
|---|---|
| Feature 2.3 sinaliza "não avaliado" (fail-open) | Não classificar; repassar a marca de "sem classificação" para a feature 2.5. |
| Score fora do intervalo esperado (ex.: negativo ou >100, por erro upstream) | Tratar como equivalente ao fail-open (sem classificação, revisão manual), com motivo `ScoreForaDeFaixa` + alerta técnico de severidade alta (indica bug, não indisponibilidade esperada). |
| Configuração de limiares ausente ou corrompida | Marcar sem classificação (motivo `ConfigIndisponivel`/`ConfigCorrompida`) + emitir alerta técnico. A resiliência "usar a última versão válida conhecida" é responsabilidade da camada de configuração versionada (feature 2.3), não desta feature — esta feature não duplica a resolução de config. |

## 16. Critérios de Aceite (Gherkin)

```gherkin
Funcionalidade: Classificação de risco e explicação textual

  Cenário: Score é classificado na faixa correta
    Dado um score de 72 calculado pela feature de score
    Quando a feature de classificação processa o caso
    Então a faixa atribuída deve ser "alto"

  Cenário: Score no limite exato pertence à faixa superior do intervalo fechado
    Dado um score de exatamente 30
    Quando a feature de classificação processa o caso
    Então a faixa atribuída deve ser "médio", não "baixo"

  Cenário: Explicação textual nomeia os sinais ativados sem linguagem acusatória
    Dado um score com os sinais de reuso de imagem e IMEI×série ativados
    Quando a explicação textual é gerada
    Então o texto deve nomear esses sinais em linguagem de indício
    E o texto não deve afirmar fraude como fato consumado

  Cenário: Cobertura parcial é mencionada na explicação
    Dado um score calculado com um sinal ausente e pesos renormalizados
    Quando a explicação textual é gerada
    Então o texto deve mencionar explicitamente que o cálculo teve cobertura parcial

  Cenário: Fail-open upstream não gera classificação inventada
    Dado que a feature de score sinaliza "não avaliado" por indisponibilidade
    Quando o caso chega à feature de classificação
    Então nenhuma faixa deve ser atribuída
    E a marca de "sem classificação" deve ser repassada para a feature de roteamento

  Cenário: Score fora do intervalo esperado é tratado como anomalia técnica
    Dado um score recebido de valor -5 (fora do intervalo [0,100])
    Quando a feature de classificação processa o caso
    Então nenhuma faixa deve ser atribuída
    E um alerta técnico de severidade alta deve ser emitido

  Cenário: Classificação é determinística
    Dado um score fixo e uma versão fixa de limiares
    Quando a classificação é executada duas vezes para essa mesma entrada
    Então a faixa e a explicação resultantes devem ser idênticas nas duas execuções

  Cenário: Auditoria registra a versão dos limiares usada
    Dado um caso classificado pela feature
    Quando o processamento é concluído
    Então o sistema deve registrar a faixa, a explicação e a versão dos limiares usada
    E esse registro deve ser imutável
```

## 17. KPIs

- Distribuição de casos por faixa (baixo/médio/alto) ao longo do tempo.
- Taxa de casos com "sem classificação" (fail-open upstream) — mede o quanto o fail-open geral está impactando a cobertura de classificação.
- Taxa de casos com cobertura parcial mencionada na explicação.
- Consistência: taxa de discrepância caso a mesma entrada seja classificada em contextos diferentes (deve ser zero, dado o requisito de determinismo).

## 18. Riscos

| Risco | Mitigação |
|---|---|
| Explicação por template soa genérica demais e não ajuda o analista | Iterar o template com feedback real de analistas; manter versionamento do template para comparar efetividade |
| Nomear sinais específicos na explicação, mesmo em linguagem de indício, ser percebido como acusatório pelo cliente caso vaze do contexto interno | Explicação é destinada exclusivamente ao painel do analista (backoffice); revisão de compliance sobre o texto final dos templates |
| Mudança de limiares na feature 2.3 e nesta feature ficarem dessincronizadas, se implementadas como componentes separados | Usar a mesma fonte única de configuração versionada para ambas |

## 19. Dependências

- Acesso à mesma configuração versionada de limiares usada pela feature 2.3 (fonte única, não duplicada).
- Conjunto de templates de explicação textual, validados previamente com a equipe antifraude/compliance quanto a não-acusação, incluindo os nomes de exibição de cada sinal (ex.: "reuso de imagem", "inconsistência de IMEI×série", "alta frequência de sinistros").
- Canal de alerta técnico de severidade alta (ex.: página de plantão) distinto do canal de alerta operacional padrão.

## 20. Itens Fora do Escopo (desta feature)

- Cálculo do score numérico (feature 2.3).
- Decisão de fila/roteamento (feature 2.5).
- Montagem do alerta completo com evidência detalhada por sinal e sugestão de prioridade (feature 2.6).
- Qualquer geração de linguagem natural livre (LLM) para a explicação — nesta fase, é sempre template determinístico.

## 21. Roadmap Futuro

1. Explicações mais ricas e contextualizadas, potencialmente com apoio de geração de linguagem natural (LLM) — desde que mantendo auditabilidade e não-acusação, possivelmente com validação humana do template gerado antes de publicar em produção.
2. Testes A/B de diferentes formulações de explicação para medir compreensão do analista.
3. Exposição de métricas de classificação em um painel de compliance dedicado.

## 22. Glossário

| Termo | Definição |
|---|---|
| **Faixa de risco** | Classificação do score em baixo, médio ou alto, conforme limiares configurados. |
| **Template determinístico** | Estrutura de texto fixa com campos variáveis preenchidos a partir dos dados do caso, garantindo que a mesma entrada sempre gere a mesma saída. |
| **Cobertura parcial** | Indicação de que o score foi calculado com pesos renormalizados por ausência de um ou mais sinais. |
| **Intervalo fechado-aberto** | Convenção matemática usada para definir sem ambiguidade a qual faixa pertence um valor exatamente no limite (ex.: [30, 70) inclui 30 mas não 70). |
| **Motivo de sem-classificação** | Enum tipado (`MotivoSemClassificacao`) que distingue *por que* um caso não recebeu faixa — indisponibilidade esperada vs. anomalia técnica — governando o disparo (ou não) do alerta. |
| **Sinal ativado** | Sinal presente no cálculo com `Valor > 0` (contribuiu de fato ao score). Critério usado para decidir quais sinais nomear na explicação. |

## 23. Decisões de Implementação e Fronteiras (sessão de grilling — 2026-07-08)

> Contexto: as features **2.2 (Coleta de Sinais)** e **2.3 (Score & Regras)** estão sendo implementadas **em paralelo por outros devs**. Nada nesta fatia pode interferir no trabalho deles — em particular, a 2.4 **não altera** o contrato de score (`IScoreProvider`, `Sinal`, `Sinistro`, `ScoringConfig`).

### 23.1 Estado atual vs. novo

Boa parte da classificação **já existe** na fundação: `Classificador.FaixaPara` (score → faixa com intervalo fechado-aberto, limiares da `ScoringConfig` versionada), `Classificador.RotaPara` (faixa → rota), `Faixa.Indeterminado` para fail-open, e o carimbo de `VersaoConfig` no `Caso` e na `RegistroAuditoria`. O trabalho **novo** da 2.4 é: (a) o **gerador de explicação textual**, (b) a resolução do **conflito do clamp** (RF09), e (c) a **distinção tipada** entre indisponibilidade esperada e anomalia técnica.

### 23.2 Decisões

| # | Decisão | Resolução |
|---|---------|-----------|
| 1 | **Placement** | Estender o `Core`: novo `GeradorDeExplicacao` puro; a explicação vira campo do `ResultadoDecisao`. Sem novo serviço/processo. |
| 2 | **Score fora de [0,100]** | **Remover o `Math.Clamp`** (`MotorDeDecisao.cs`) e tratar como **anomalia**: sem classificação (rota de revisão manual) + alerta severidade alta. |
| 3 | **Canal de alerta** | Nova porta `IAlertaTecnico` no `Core` + adapter na `Infra` que loga estruturado (nível Critical) com o `caseId`. Gancho pronto para plantão/PagerDuty; sem construir o canal real agora. |
| 4 | **Distinguir "sem classificação"** | Novo enum `MotivoSemClassificacao` (ex.: `SinalAusente`, `ProviderIndisponivel`, `ConfigIndisponivel`, `ConfigCorrompida`, `ScoreForaDeFaixa`), carimbado no `Caso` e na `Auditoria`, ao lado da `Causa` textual. O disparo do alerta é decidido **pelo motivo**, não por parse de string. |
| 5 | **Persistir a explicação** | Coluna nova no `Caso` **e** no `RegistroAuditoria` (migration aditiva). |
| 6 | **Template** | Em código, determinístico, com `VersaoTemplate` carimbada ao lado da versão de limiares. Compliance revisa por PR/diff. |
| 7 | **Nomes de exibição dos sinais** | Mapa em código no módulo de template (versionado junto), com fallback seguro para sinal desconhecido — nunca vaza o id técnico cru. |
| 8 | **"Sinal ativado"** | Presente com `Valor > 0`. |
| 9 | **Texto em fail-open (RF06)** | Explicação de faixa fica `null` (nenhuma faixa/texto de faixa inventado). A "marca" é o `MotivoSemClassificacao`, que expõe um **rótulo canônico** curto e não-acusatório derivado do enum, produzido num único lugar no `Core` (consistência entre consumidores). |
| 10 | **Cobertura parcial (RF05)** | `GeradorDeExplicacao` recebe `coberturaParcial` como **parâmetro próprio**; o seam passa `false` até a 2.3 expor o dado. **Sem tocar `IScoreProvider`.** O galho do template é coberto por unit test desde já. |
| 11 | **Integração** | **Aditivo** (só arquivos novos) + **um** seam mínimo no `MotorDeDecisao` (chamar o gerador; mover o tratamento de out-of-range para cá). O ponto de merge é combinado com o dev da 2.3. |
| 12 | **Verificação** | Unit (gerador + classificador com entradas sintéticas) **+** integração com um `IScoreProvider` de teste **no projeto Tests** (injeta sinais e scores fora de faixa). **Não** toca o `MockScoreProvider` compartilhado. |
| 13 | **Config corrompida (§15)** | 2.4 marca o motivo + emite o alerta; a resiliência "última versão válida conhecida" fica com a camada de config (2.3). |

### 23.3 Derivados (sem conflito)

- `RotaPara` fica como está: anomalia e fail-open → `Faixa.Indeterminado` → `Rota.Reforcada` (revisão manual). Quem distingue os casos é o `MotivoSemClassificacao`, não a rota.
- Rótulos canônicos (dec. 9) e nomes de exibição (dec. 7) moram no mesmo módulo de template, sob a mesma `VersaoTemplate`, sujeitos à mesma revisão de compliance.

### 23.4 Reachability hoje (nota de verificação)

No pipeline vivo atual, **todo caso cai em fail-open** — os sinais não fluem (2.2 pendente, `Sinistro.SinaisIncompletos` curto-circuita antes do score) e o `MockScoreProvider` também clampa. Portanto os destaques da 2.4 (explicação de faixa real, nomear sinais, cobertura parcial, anomalia out-of-range) só são exercitáveis por **unit test** e pelo **test-double** da decisão 12 — não pelo caminho end-to-end padrão enquanto 2.2/2.3 não assentarem.
