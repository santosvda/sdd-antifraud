# PRD — Motor Antifraude de Sinistros
## Feature 2.5: Roteamento
**ACME Seguros — Seguro de Celular — Trilha B (sinistro por foto)**

---

## 1. Visão Geral

Esta feature é o **ponto de decisão de fila**: recebe a faixa de risco (ou a marca de "sem classificação") produzida pela feature 2.4, e decide para qual fila o caso vai — **normal**, **reforçada**, ou **revisão manual (não avaliado por IA)**. É a feature que materializa, na prática, o guardrail mais importante do produto inteiro: **em nenhuma hipótese o sinistro é bloqueado, negado ou aprovado automaticamente** — o roteamento decide apenas *a ordem e a fila* em que um humano vai olhar o caso.

Esta feature também é responsável por lidar com a **saturação da fila reforçada** (quando o volume de casos de alto risco excede a capacidade de análise) sem nunca liberar um caso por conta disso.

Esta feature **não** decide o mérito do sinistro, **não** monta o alerta completo com evidência (feature 2.6) e **não** calcula score ou faixa — ela apenas roteia com base no que já chegou classificado.

## 2. Problema

Sem uma camada de roteamento explícita, cada consumidor (painel, fila) teria que reimplementar a lógica de "que faixa vai pra onde" — com risco de inconsistência entre uma faixa alta ser tratada como prioritária num lugar e não em outro, e sem um mecanismo formal para lidar com o cenário realista de pico de fraude saturando a capacidade de análise reforçada.

## 3. Objetivos

- Rotear cada caso para a fila correta (normal, reforçada, ou revisão manual) com base na faixa de risco recebida.
- Garantir, por construção, que nenhum roteamento resulte em bloqueio, negação ou aprovação automática do sinistro.
- Tratar de forma explícita e auditável os casos que chegam sem classificação (fail-open da feature 2.3, ou anomalia técnica da feature 2.4), direcionando-os para revisão manual.
- Sub-priorizar a fila reforçada quando ela saturar, sem nunca liberar um caso automaticamente por estouro de SLA.
- Deixar rastro auditável de para onde cada caso foi roteado e por quê.

**Não-objetivos desta feature:** decidir mérito do sinistro, montar o alerta completo com evidência (feature 2.6), calcular score ou faixa.

## 4. Personas

| Persona | Papel nesta feature |
|---|---|
| **Feature 2.4 (Classificação de Risco)** | Produtora da entrada desta feature (faixa, ou marca de "sem classificação"). |
| **Analista de sinistro** | Consumidor final das filas — opera a partir da priorização definida aqui. |
| **Equipe antifraude** | Define/calibra o mapeamento faixa → fila e os parâmetros de saturação. |
| **Operação/Plantão técnico** | Recebe alertas de saturação de fila e de anomalias técnicas escaladas. |

## 5. Jornada (Analista / Equipe Antifraude)

1. O caso chega com uma faixa de risco (baixo/médio/alto) ou marcado como "sem classificação".
2. Esta feature aplica o mapeamento configurável de faixa → fila e roteia o caso.
3. Se o caso está "sem classificação" (fail-open ou anomalia técnica), vai direto para a fila de revisão manual, distinta da fila reforçada.
4. Se a fila reforçada estiver saturada (acima da capacidade configurada), os casos são sub-priorizados internamente (score desc., depois valor do sinistro) e um alerta operacional de capacidade é disparado — nenhum caso é liberado ou rebaixado de fila por isso.
5. Analista opera cada fila normalmente; equipe antifraude acompanha, via métricas, se o mapeamento faixa→fila está gerando volume saudável em cada fila, ajustando a calibração quando necessário.

## 6. Fluxo Completo (com saturação e fail-open)

```
[Faixa de risco ou "sem classificação"] (vindo da Feature 2.4)
                │
                ▼
   ┌─────────────────────────────┐
   │ Caso tem faixa classificada?  │
   └───────┬───────────────┬─────┘
       sim │               │ não (fail-open ou
           ▼               │  anomalia técnica)
┌─────────────────────┐    ▼
│ Mapeamento Faixa→Fila │  ┌───────────────────────────┐
│ (configurável):        │  │ Fila de Revisão Manual       │
│  baixo  → normal        │  │ ("não avaliado por IA")      │
│  médio  → normal*       │  │ — sinistro segue seu curso,  │
│  alto   → reforçada     │  │ analista vê sem score/faixa  │
└──────────┬─────────────┘  └───────────────────────────┘
           ▼
┌─────────────────────┐
│ Fila destino =        │
│ normal ou reforçada?  │
└───┬───────────────┬──┘
normal│           reforçada│
    ▼                   ▼
┌──────────┐  ┌─────────────────────────────┐
│ Fila       │  │ Fila Reforçada está saturada?  │
│ Normal     │  │ (> 20 casos pendentes)          │
└──────────┘  └───────┬───────────────┬─────┘
                        │ não             │ sim
                        ▼               ▼
              ┌──────────────┐  ┌───────────────────────────┐
              │ Entra na fila  │  │ Sub-prioriza internamente:  │
              │ reforçada       │  │ score desc., depois valor    │
              │ normalmente     │  │ do sinistro. Caso permanece  │
              └──────────────┘  │ na fila reforçada (nunca é    │
                                 │ liberado/rebaixado)            │
                                 │ + dispara alerta operacional   │
                                 │ de capacidade se SLA de 4h de  │
                                 │ espera for excedido            │
                                 └───────────────────────────┘
                        │
                        ▼
              ┌─────────────────────────────┐
              │ Registro de Auditoria          │
              │ (fila de destino, motivo,       │
              │ sub-prioridade se aplicável)    │
              └─────────────────────────────┘

* faixa "médio" tem mapeamento configurável — pode ir para fila reforçada
  dependendo de calibração da equipe antifraude (ver Regras de Negócio).
```

**Ponto crítico do guardrail:** em nenhum ramo deste fluxo o sinistro é bloqueado, negado ou aprovado — a única decisão tomada é **qual fila** e **em que posição** dentro dela.

## 7. Regras de Negócio

1. O mapeamento faixa → fila é **configurável**, nunca hard-coded. Padrão inicial: baixo → fila normal, médio → fila normal, alto → fila reforçada. A equipe antifraude pode recalibrar para que "médio" também vá para a fila reforçada, conforme volume e capacidade reais.
2. Casos sem classificação (fail-open da feature 2.3, ou anomalia técnica sinalizada pela feature 2.4) vão para a **fila de revisão manual ("não avaliado por IA")**, distinta da fila reforçada — o sinistro segue seu curso normalmente, apenas sem score/faixa disponível para o analista.
3. **Nenhum roteamento, em nenhuma hipótese, bloqueia, nega ou aprova o sinistro.** Esta é a regra estrutural inegociável da feature.
4. Quando a fila reforçada satura (**acima de 20 casos pendentes**, valor configurável), os casos são **sub-priorizados internamente**: primeiro por score (decrescente), depois por valor do sinistro (decrescente) como critério de desempate.
5. Se um caso na fila reforçada ultrapassar o **SLA máximo de espera de 4 horas** (valor configurável), o sistema dispara um **alerta operacional de capacidade** (para escalar mais analistas) — o caso **nunca** é liberado, rebaixado de fila ou processado automaticamente por causa do estouro de SLA.
6. Toda mudança no mapeamento faixa→fila ou nos parâmetros de saturação/SLA é um evento de auditoria versionado, sob a mesma governança da equipe antifraude já estabelecida para os limiares de score.

## 8. Arquitetura de Alto Nível

```
┌─────────────────────────┐
│ Feature 2.4                │
│ (Classificação de Risco)   │
└────────────┬──────────────┘
             │ faixa (ou "sem classificação")
             ▼
┌──────────────────────────────────────┐
│ FEATURE 2.5 — Roteamento                 │
│                                          │
│  ┌───────────────────────────┐         │
│  │ Roteador por Faixa            │◀────┼── Configuração de mapeamento
│  │ (mapeamento configurável)     │      │   faixa → fila (versionada)
│  └─────────────┬─────────────────┘      │
│                ▼                        │
│  ┌───────────────────────────┐         │
│  │ Monitor de Saturação           │◀────┼── Configuração de capacidade
│  │ (sub-priorização + SLA)        │      │   e SLA de espera (versionada)
│  └─────────────┬─────────────────┘      │
│                ▼                        │
│  ┌───────────────────────────┐         │
│  │ Publicador de Auditoria        │──────┼──▶ Log imutável
│  └─────────────┬─────────────────┘      │
└────────────────┼─────────────────────────┘
                 ▼
   ┌──────────┐ ┌─────────────────┐ ┌───────────────────────────┐
   │ Fila       │ │ Fila Reforçada     │ │ Fila de Revisão Manual        │
   │ Normal     │ │                    │ │ ("não avaliado por IA")       │
   └──────────┘ └─────────────────┘ └───────────────────────────┘
                        │
                        ▼
              Painel do Analista (fora de escopo — feature futura)
```

## 9. Requisitos Funcionais

| ID | Requisito |
|---|---|
| RF01 | O sistema deve rotear cada caso para fila normal, reforçada ou revisão manual, conforme o mapeamento configurável de faixa → fila. |
| RF02 | O sistema deve tratar casos "sem classificação" roteando-os para a fila de revisão manual, distinta da fila reforçada. |
| RF03 | O sistema nunca deve bloquear, negar ou aprovar o sinistro em nenhuma etapa do roteamento. |
| RF04 | O sistema deve permitir configurar o mapeamento faixa → fila sem alteração de código, incluindo a possibilidade de "médio" ir para fila reforçada. |
| RF05 | O sistema deve detectar quando a fila reforçada está saturada, considerando saturação como mais de 20 casos pendentes (valor configurável). |
| RF06 | Em saturação, o sistema deve sub-priorizar os casos por score (decrescente) e depois por valor do sinistro (decrescente). |
| RF07 | O sistema deve disparar alerta operacional de capacidade quando um caso exceder o SLA máximo de espera de 4 horas (valor configurável) na fila reforçada, sem liberar o caso. |
| RF08 | O sistema deve registrar, para cada caso, a fila de destino, o motivo do roteamento e a posição de sub-prioridade quando aplicável. |

## 10. Requisitos Não Funcionais

- **Assíncrono:** roteamento não impõe espera síncrona a nenhuma etapa anterior do pipeline.
- **Latência:** decisão de roteamento é uma operação leve; não deve adicionar latência perceptível ao orçamento de SLA (≤5 min p95 total, ponta a ponta).
- **Auditabilidade:** toda decisão de roteamento gera registro imutável.
- **Observabilidade:** métricas de volume por fila, taxa de saturação, taxa de casos em revisão manual (fail-open + anomalia técnica).
- **Escalabilidade horizontal:** suporta picos de volume sem degradar a decisão de roteamento em si (a saturação é tratada como sinal operacional, não como falha técnica).

## 11. Integrações

- **Feature 2.4 (Classificação de Risco)** — fonte da faixa (ou marca de "sem classificação").
- **Serviço de Configuração** — mapeamento faixa→fila, capacidade da fila reforçada e SLA de espera, todos versionados.
- **Base de Apólices/Sinistros** — fonte do valor do sinistro, usado como critério de desempate na sub-priorização.
- **Sistema de filas (normal, reforçada, revisão manual)** — destino do roteamento; consumido posteriormente pelo painel do analista (feature futura).
- **Canal de alerta operacional** — para notificar saturação de capacidade.
- **Log/Auditoria** — armazenamento imutável de cada decisão de roteamento.

## 12. Segurança e LGPD

- Esta feature não introduz novo tratamento de dados pessoais além dos identificadores técnicos já em trânsito (ID do sinistro, faixa, score, valor do sinistro para desempate).
- Acesso às filas segue a segregação já definida entre analista e compliance.
- Nenhuma decisão de roteamento é baseada em atributos sensíveis proibidos — a entrada desta feature já é a faixa (derivada de um score que não os utiliza).

## 13. Auditoria

Para cada caso roteado, registrar de forma imutável:
- ID do sinistro, faixa recebida (ou marca de "sem classificação").
- Fila de destino e motivo do roteamento (mapeamento aplicado).
- Versão da configuração de mapeamento/capacidade/SLA usada.
- Posição de sub-prioridade e critério de desempate, quando aplicável (situação de saturação).
- Timestamp da decisão de roteamento.

## 14. Casos de Uso

1. **Faixa baixa:** roteada para fila normal, sem alerta de saturação.
2. **Faixa alta:** roteada para fila reforçada, com prioridade conforme score.
3. **Faixa média, mapeamento padrão:** roteada para fila normal.
4. **Faixa média, mapeamento recalibrado pela equipe antifraude:** roteada para fila reforçada.
5. **Sem classificação (fail-open ou anomalia técnica):** roteada para fila de revisão manual, distinta da reforçada.
6. **Fila reforçada saturada:** mais de 20 casos de faixa alta pendentes simultaneamente → sub-priorização por score e valor do sinistro, com alerta operacional de capacidade disparado se algum caso ultrapassar 4 horas de espera.

## 15. Casos de Exceção

| Exceção | Tratamento |
|---|---|
| Caso chega sem faixa (fail-open ou anomalia técnica) | Roteado para fila de revisão manual, distinta da fila reforçada. |
| Fila reforçada saturada (>20 casos pendentes) | Sub-priorização por score/valor do sinistro; alerta operacional de capacidade se SLA de 4h de espera for excedido; nenhum caso liberado automaticamente. |
| Valor do sinistro indisponível (fonte externa fora do ar) | Sub-priorização usa apenas o score como critério; valor do sinistro entra como critério de desempate apenas quando disponível. |
| Configuração de mapeamento/capacidade/SLA ausente ou corrompida | Usar a última versão válida conhecida; emitir alerta técnico; nunca operar sem configuração validada. |
| Fila de destino (normal/reforçada/revisão manual) indisponível | Retry com backoff; se persistir, escalar para alerta técnico — o caso nunca é descartado, apenas atrasado no enfileiramento. |

## 16. Critérios de Aceite (Gherkin)

```gherkin
Funcionalidade: Roteamento por faixa de risco, sem bloqueio, com sub-priorização em saturação

  Cenário: Faixa alta roteia para fila reforçada sem bloquear o sinistro
    Dado um caso classificado na faixa "alto"
    Quando a feature de roteamento processa o caso
    Então o caso deve ser roteado para a fila reforçada
    E o sinistro não deve ser bloqueado, negado ou aprovado automaticamente

  Cenário: Faixa baixa roteia para fila normal
    Dado um caso classificado na faixa "baixo"
    Quando a feature de roteamento processa o caso
    Então o caso deve ser roteado para a fila normal

  Cenário: Caso sem classificação vai para fila de revisão manual
    Dado um caso que chega sem faixa classificada (fail-open ou anomalia técnica)
    Quando a feature de roteamento processa o caso
    Então o caso deve ser roteado para a fila de revisão manual
    E essa fila deve ser distinta da fila reforçada

  Cenário: Mapeamento de faixa "médio" é configurável
    Dado que a equipe antifraude reconfigura o mapeamento para que a faixa "médio" vá para a fila reforçada
    Quando um caso de faixa "médio" é processado após a mudança
    Então o caso deve ser roteado para a fila reforçada
    E o registro de auditoria deve indicar a versão do mapeamento usada

  Cenário: Fila reforçada saturada sub-prioriza sem liberar casos
    Dado que a fila reforçada tem mais de 20 casos pendentes
    Quando novos casos de faixa alta chegam
    Então os casos devem ser sub-priorizados por score decrescente e depois por valor do sinistro
    E nenhum caso deve ser liberado, rebaixado de fila ou processado automaticamente

  Cenário: Estouro de SLA de 4 horas na fila reforçada dispara alerta, não libera o caso
    Dado que um caso na fila reforçada ultrapassa 4 horas de espera
    Quando o sistema verifica o tempo de espera
    Então um alerta operacional de capacidade deve ser disparado
    E o caso deve permanecer na fila reforçada

  Cenário: Nenhum caso é bloqueado ou negado automaticamente, independente da fila
    Dado qualquer caso roteado pela feature, para qualquer fila
    Quando o roteamento é concluído
    Então o sinistro deve permanecer disponível para seguimento normal do processo de sinistro
    E nenhuma ação de bloqueio, negação ou aprovação automática deve ocorrer

  Cenário: Auditoria registra a decisão completa de roteamento
    Dado um caso processado pela feature de roteamento
    Quando o roteamento é concluído
    Então o sistema deve registrar a fila de destino, o motivo, a versão da configuração e a sub-prioridade se aplicável
    E esse registro deve ser imutável
```

## 17. KPIs

- Volume de casos por fila (normal, reforçada, revisão manual).
- Taxa de saturação da fila reforçada (frequência e duração).
- Taxa de estouro de SLA de espera na fila reforçada.
- Taxa de casos em revisão manual (proxy de quanto o fail-open geral está impactando a cobertura de análise).
- Tempo médio de espera por fila.

## 18. Riscos

| Risco | Mitigação |
|---|---|
| Mapeamento "médio → reforçada" mal calibrado sobrecarrega a fila reforçada permanentemente | Monitorar taxa de saturação após qualquer mudança de mapeamento; gatilho reativo de recalibração |
| Fonte de valor do sinistro indisponível compromete a qualidade do desempate em saturação | Degradar graciosamente para sub-priorização só por score, sem bloquear o roteamento |
| Alertas de saturação recorrentes viram "ruído" e deixam de ser acionados pela operação | Definir threshold de SLA realista com a operação; revisar periodicamente junto com a calibração de limiares |

## 19. Dependências

- Definição do mapeamento faixa→fila padrão como configuração versionada inicial (v1): baixo→normal, médio→normal, alto→reforçada.
- Acesso à fonte de valor do sinistro para uso como critério de desempate.
- Infraestrutura de contagem de casos pendentes em tempo real na fila reforçada, para avaliar o limiar de saturação (20 casos).

## 20. Itens Fora do Escopo (desta feature)

- Cálculo de score e classificação de faixa (features 2.3 e 2.4).
- Montagem do alerta completo com evidência por sinal (feature 2.6).
- UI do painel do analista (feature futura).
- Redistribuição de analistas ou gestão de capacidade humana (é decisão operacional, não desta feature — a feature apenas alerta).

## 21. Roadmap Futuro

1. Redistribuição automática sugerida entre analistas conforme carga (mantendo a decisão de alocação humana).
2. Painel operacional dedicado à saúde das filas (saturação, SLA, volume) para a equipe antifraude e operação.
3. Critérios de sub-priorização adicionais além de score e valor do sinistro (ex.: tempo de espera acumulado como terceiro critério de desempate).

## 22. Glossário

| Termo | Definição |
|---|---|
| **Fila normal** | Fila de análise padrão, para casos de risco baixo ou médio (conforme mapeamento). |
| **Fila reforçada** | Fila de análise prioritária para casos classificados como alto risco (ou médio, se recalibrado). |
| **Fila de revisão manual** | Fila para casos sem classificação de risco disponível (fail-open ou anomalia técnica), distinta da fila reforçada. |
| **Saturação** | Situação em que o volume de casos na fila reforçada excede a capacidade configurada para análise no SLA esperado. |
| **Sub-priorização** | Reordenação interna dos casos dentro de uma fila saturada, por score e depois valor do sinistro, sem alterar a fila de destino. |
| **SLA de espera** | Tempo máximo que um caso pode aguardar na fila reforçada antes de disparar um alerta operacional de capacidade. |
