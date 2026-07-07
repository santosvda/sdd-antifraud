# PRD — Motor Antifraude de Sinistros
## Feature 2.9: Auditoria & Observabilidade (Transversal)
**ACME Seguros — Seguro de Celular — Trilha B (sinistro por foto)**

---

## 1. Visão Geral

Esta feature é **transversal**: diferente das features 2.1 a 2.8, que formam um pipeline sequencial, esta é a **camada comum** que todas elas usam para registrar eventos de auditoria e expor observabilidade operacional. Ela formaliza um contrato único de log — cada feature anterior já foi desenhada assumindo que existe um "Registro de Auditoria" e um "Log imutável"; esta PRD é onde esse contrato deixa de ser implícito e se torna explícito, padronizado e consultável.

Três capacidades centrais: **(1)** um log estruturado, **imutável (append-only)**, por caso, unificando o que cada feature já registra individualmente numa **trilha consolidada** (a jornada completa do sinistro, do evento de abertura até o feedback do analista); **(2)** **observabilidade fim a fim** (latência, disponibilidade das dependências, saúde das filas); **(3)** um **painel de compliance** com acesso segregado do painel do analista (feature 2.7).

## 2. Problema

Sem uma camada transversal formal, cada feature (2.1 a 2.8) implementaria seu próprio mecanismo de log de forma isolada — o que já vem acontecendo nas PRDs anteriores, cada uma descrevendo seu "Registro de Auditoria" próprio. Isso funciona feature a feature, mas impede reconstituir a **jornada completa** de um sinistro (a pergunta "o que aconteceu com o caso X, do início ao fim?" exigiria consultar 8 lugares diferentes), dificulta a operação (não há visão única de SLA ponta a ponta) e enfraquece a auditoria de compliance (que precisa da trilha inteira, não de fragmentos).

## 3. Objetivos

- Padronizar o formato dos eventos de auditoria entre as 8 features do motor, sem duplicar o que cada uma já registra — esta feature consolida, não substitui.
- Garantir imutabilidade real (append-only): nenhum registro é alterado ou apagado após escrito.
- Oferecer uma **trilha consolidada por sinistro**, reconstituindo a jornada completa na ordem correta.
- Medir observabilidade ponta a ponta: latência total (SLA ≤5 min p95), disponibilidade das dependências (serviço de score, filas, fontes de sinais) e saúde operacional das filas de roteamento.
- Expor um **painel de compliance**, com acesso segregado do painel do analista — compliance vê a trilha completa e métricas agregadas; o analista vê apenas seus próprios casos (já coberto pela feature 2.7).

**Não-objetivos desta feature:** calcular qualquer sinal, score, faixa ou roteamento (isso pertence às features 2.1–2.8); tomar qualquer ação corretiva automática a partir de um alerta operacional (a resposta a um alerta é sempre humana).

## 4. Personas

| Persona | Papel nesta feature |
|---|---|
| **Todas as features 2.1–2.8** | Produtoras de eventos de auditoria, consumindo o contrato comum definido aqui. |
| **Compliance** | Usuária primária do painel de compliance — consulta a trilha consolidada e métricas agregadas. |
| **Equipe antifraude** | Consulta métricas de calibração (já detalhadas nas features 2.3/2.8) através da mesma camada de observabilidade. |
| **Operação / SRE** | Monitora latência, disponibilidade e saúde das filas para resposta operacional a incidentes. |

## 5. Jornada (Compliance)

1. Compliance abre o painel de compliance e busca por um sinistro específico (ou por um período/filtro).
2. O painel exibe a **trilha consolidada**: ingestão → coleta de sinais → score → classificação → roteamento → alerta → decisão do analista → feedback, cada etapa com seu timestamp, dados relevantes e versão de configuração usada.
3. Se alguma etapa está ausente na trilha (ex.: por uma falha upstream), o painel indica isso explicitamente como uma **lacuna**, nunca preenchendo silenciosamente.
4. Separadamente, compliance e operação consultam dashboards de observabilidade: latência ponta a ponta, disponibilidade de dependências, volume e saturação das filas.
5. Se algo estiver fora do esperado (SLA estourando, dependência instável), o alerta operacional já definido nas features anteriores (ex.: fail-open, saturação de fila) aparece consolidado aqui também, para visão única.

## 6. Fluxo Completo (com lacuna de trilha)

```
[Evento de auditoria de cada feature] (2.1 → 2.8, cada uma publicando
 seu próprio registro conforme já definido em sua PRD)
                │
                ▼
┌─────────────────────────────┐
│ Validador de Esquema Comum      │  ← rejeita eventos fora do formato
│ (schema padronizado)            │    padronizado (nunca aceita
└───────────────┬────────────────┘    estrutura ad-hoc)
                ▼
┌─────────────────────────────┐
│ Armazenamento Append-Only       │  ← nenhuma escrita altera ou
│ (nunca update, nunca delete)    │    remove um registro existente
└───────────────┬────────────────┘
                ▼
┌─────────────────────────────┐
│ Indexador por ID de Sinistro     │
└───────────────┬────────────────┘
                ▼
   ┌─────────────────────────────┐
   │ Todas as 8 etapas presentes    │
   │ (completas ou "n/a" explícito) │
   │ para este sinistro?             │
   └───────┬───────────────┬─────┘
       sim │               │ não (ausência sem
           ▼               │  explicação registrada)
┌──────────────────┐  ┌───────────────────────────┐
│ Trilha Consolidada  │  │ Trilha Consolidada            │
│ Completa             │  │ com Lacuna Inesperada           │
│ (inclui etapas "n/a" │  │ (indica qual etapa está        │
│ de fail-open, sem     │  │ ausente sem explicação,        │
│ tratar como problema) │  │ nunca preenche, gera alerta)   │
└──────────────────┘  └───────────────────────────┘
           │                       │
           └───────────┬───────────┘
                       ▼
          ┌─────────────────────────────┐
          │ Painel de Compliance            │
          │ (acesso segregado do painel      │
          │ do analista)                     │
          └─────────────────────────────┘

  ── Em paralelo, trilha independente de observabilidade técnica ──
[Instrumentação de cada feature] → Latência por etapa, disponibilidade
de dependências, volume/saturação de filas → Dashboards de Observabilidade
→ Alertas operacionais (SLA, indisponibilidade, saturação)
```

**Ponto crítico do guardrail:** uma lacuna na trilha **nunca é preenchida ou inferida** — é sempre exibida como ausência explícita, para não criar uma falsa sensação de completude num registro que é, por definição, a fonte de verdade para auditoria.

## 7. Regras de Negócio

1. Todo evento de auditoria publicado por qualquer feature (2.1–2.8) segue um **esquema comum padronizado**: identificador do sinistro, feature de origem, timestamp, tipo de evento, payload específico (já detalhado individualmente em cada PRD anterior), e versão de configuração relevante quando aplicável.
2. O armazenamento é **append-only**: nenhum registro é alterado ou apagado após escrito. Uma correção necessária gera um **novo registro de retificação**, que referencia o registro original — nunca uma substituição.
3. A trilha consolidada por sinistro reconstitui a jornada completa, ordenada por timestamp, cobrindo as 8 etapas do pipeline (2.1 a 2.8).
4. Uma etapa pulada por um caminho já documentado pela própria feature de origem (ex.: fail-open na feature 2.3, anomalia técnica na 2.4) **não é tratada como lacuna** — é registrada como etapa completa com valor **"não aplicável (n/a)"**, já que a ausência foi explicada pela própria feature no momento em que ocorreu. **Lacuna** é reservada exclusivamente para ausência real e inexplicada (evento perdido ou falha de infraestrutura) — apenas esse segundo caso é exibido como lacuna e gera alerta operacional.
5. O acesso ao painel de compliance é **segregado** do painel do analista (feature 2.7): compliance vê a trilha completa de qualquer sinistro e métricas agregadas de todas as features; o analista vê apenas os casos atribuídos a ele, através do painel próprio já definido na feature 2.7.
6. A observabilidade técnica (latência, disponibilidade, saturação de filas) é medida e exposta separadamente da trilha de auditoria — são naturezas diferentes de dado: a trilha é o registro legal/imutável por caso; a observabilidade é a métrica operacional agregada, usada para resposta a incidentes.
7. Nenhum evento de auditoria pode conter atributos sensíveis proibidos ou dados pessoais além do que cada feature de origem já determinou como necessário — esta feature valida contra o esquema comum, mas não decide o que cada feature envia.

## 8. Arquitetura de Alto Nível

```
┌───────┐ ┌───────┐ ┌───────┐ ┌───────┐ ┌───────┐ ┌───────┐ ┌───────┐ ┌───────┐
│ 2.1     │ │ 2.2     │ │ 2.3     │ │ 2.4     │ │ 2.5     │ │ 2.6     │ │ 2.7     │ │ 2.8     │
└───┬───┘ └───┬───┘ └───┬───┘ └───┬───┘ └───┬───┘ └───┬───┘ └───┬───┘ └───┬───┘
    └─────────┴─────────┴─────────┴─────────┴─────────┴─────────┴─────────┘
                                   │ eventos de auditoria (esquema comum)
                                   ▼
                  ┌──────────────────────────────────┐
                  │ FEATURE 2.9 — Auditoria & Observabilidade │
                  │                                          │
                  │  ┌───────────────────────────┐         │
                  │  │ Validador de Esquema Comum    │        │
                  │  └─────────────┬─────────────────┘      │
                  │                ▼                        │
                  │  ┌───────────────────────────┐         │
                  │  │ Armazenamento Append-Only      │        │
                  │  └─────────────┬─────────────────┘      │
                  │                ▼                        │
                  │  ┌───────────────────────────┐         │
                  │  │ Indexador + Consolidador        │       │
                  │  │ de Trilha por Sinistro           │       │
                  │  └─────────────┬─────────────────┘      │
                  │                ▼                        │
                  │  ┌───────────────────────────┐         │
                  │  │ Painel de Compliance            │       │
                  │  └───────────────────────────┘         │
                  │                                          │
                  │  ┌───────────────────────────┐         │
                  │  │ Coletor de Métricas Técnicas   │◀──────┼── Instrumentação de
                  │  │ (latência, disponibilidade,     │      │   cada feature
                  │  │ saturação de filas)              │      │
                  │  └─────────────┬─────────────────┘      │
                  │                ▼                        │
                  │  ┌───────────────────────────┐         │
                  │  │ Dashboards + Alertas             │       │
                  │  │ Operacionais                      │       │
                  │  └───────────────────────────┘         │
                  └──────────────────────────────────┘
```

## 9. Requisitos Funcionais

| ID | Requisito |
|---|---|
| RF01 | O sistema deve validar todo evento de auditoria recebido contra um esquema comum, rejeitando eventos fora do formato. |
| RF02 | O sistema deve armazenar eventos de forma append-only, sem permitir alteração ou remoção de registros existentes. |
| RF03 | O sistema deve permitir registrar uma retificação como novo evento referenciando o original, nunca substituindo-o. |
| RF04 | O sistema deve consolidar, por ID de sinistro, a trilha completa das 8 etapas do pipeline, ordenada por timestamp. |
| RF05 | O sistema deve tratar etapas puladas por caminhos já documentados (fail-open, anomalia técnica) como "não aplicável (n/a)", nunca como lacuna; deve reservar o rótulo de lacuna e o alerta operacional exclusivamente para ausência real e inexplicada. |
| RF06 | O sistema deve segregar o acesso: painel de compliance vê trilha completa e métricas agregadas; painel do analista (feature 2.7) permanece restrito aos casos do próprio analista. |
| RF07 | O sistema deve medir latência ponta a ponta (por etapa e total), disponibilidade das dependências e saturação das filas. |
| RF08 | O sistema deve expor dashboards e alertas operacionais consolidados, reaproveitando os alertas já definidos individualmente nas features 2.1–2.8. |

## 10. Requisitos Não Funcionais

- **Durabilidade:** nenhum evento de auditoria pode ser perdido — write-ahead garantido antes de confirmar a escrita à feature de origem.
- **Imutabilidade:** garantida estruturalmente pelo armazenamento append-only, não apenas por convenção de uso.
- **Performance de consulta:** a trilha consolidada de um sinistro deve ser recuperável rapidamente o suficiente para uso interativo no painel de compliance.
- **Escalabilidade horizontal:** o volume de eventos cresce proporcionalmente ao volume de sinistros; a arquitetura deve suportar esse crescimento sem degradar a escrita.
- **Segurança:** acesso ao painel de compliance e às consultas de trilha restrito e segregado do acesso do analista.

## 11. Integrações

- **Features 2.1 a 2.8** — todas publicam eventos de auditoria no esquema comum definido aqui.
- **Feature 2.7 (Painel do Analista)** — mantém seu próprio painel e controle de acesso; esta feature não substitui, apenas garante a segregação formal em relação ao painel de compliance.
- **Ferramenta de observabilidade/dashboards** — consome as métricas técnicas coletadas.
- **Canal de alerta operacional** — já usado pelas features anteriores (fail-open, saturação de fila, anomalia técnica), consolidado aqui numa visão única.

## 12. Segurança e LGPD

- O esquema comum de auditoria não introduz novo dado pessoal além do que cada feature de origem já decidiu registrar — esta feature é uma camada de padronização e consolidação, não uma nova coleta.
- Acesso ao painel de compliance é restrito e distinto do acesso do analista, reforçando a segregação já exigida desde o brief original do produto.
- Retenção dos eventos de auditoria: **2 anos como valor inicial**, sujeito a validação jurídica/DPO antes de produção.
- Mascaramento de dados sensíveis já aplicado por cada feature de origem é preservado; esta feature não descriptografa nem expõe dado além do que já chega mascarado.

## 13. Auditoria

Esta feature **é** a camada de auditoria — mas também precisa de meta-auditoria sobre si mesma:
- Toda mudança no esquema comum de eventos é versionada e registrada.
- Todo acesso ao painel de compliance (quem consultou, quando, qual sinistro) é registrado, para evitar uso indevido da própria trilha de auditoria.
- Toda retificação de um evento existente registra o motivo e quem a solicitou.

## 14. Casos de Uso

1. **Reconstituir a jornada completa de um sinistro:** compliance busca pelo ID e vê as 8 etapas em ordem, cada uma com seus dados relevantes.
2. **Consultar uma etapa "não aplicável":** um sinistro passou por fail-open na feature 2.3 → a trilha mostra essa etapa como "n/a", com o motivo já registrado por aquela feature, sem gerar alerta.
2a. **Investigar uma lacuna inesperada:** um sinistro tem uma etapa ausente sem nenhuma explicação registrada → a trilha mostra isso como lacuna real, e um alerta operacional já foi disparado no momento da detecção.
3. **Monitorar SLA ponta a ponta:** operação consulta o dashboard e vê que a latência p95 está dentro do orçamento de 5 minutos.
4. **Detectar degradação de dependência:** disponibilidade do serviço de score cai → alerta consolidado aparece no painel de observabilidade, correlacionado com o aumento de casos em fail-open.
5. **Auditoria externa/regulatória:** compliance exporta a trilha consolidada de um conjunto de sinistros para responder a uma auditoria externa.

## 15. Casos de Exceção

| Exceção | Tratamento |
|---|---|
| Evento de auditoria chega fora do esquema comum | Rejeitado pelo validador; alerta técnico para a feature de origem corrigir a publicação. |
| Uma etapa foi pulada por fail-open ou anomalia técnica já documentado | Registrada como "não aplicável (n/a)"; não gera alerta nem é tratada como lacuna. |
| Uma etapa da trilha está ausente sem nenhuma explicação registrada | Exibida explicitamente como lacuna inesperada no painel de compliance; gera alerta operacional; nunca inferida ou preenchida. |
| Necessidade de corrigir um registro já escrito | Nunca alterado — gera-se um novo evento de retificação, referenciando o original. |
| Armazenamento append-only temporariamente indisponível | Eventos ficam em buffer com retry; nenhuma feature de origem perde o registro, mesmo que a escrita final seja adiada. |
| Volume de eventos cresce além do previsto (pico de sinistros) | Escalabilidade horizontal do armazenamento; sem impacto na garantia de durabilidade. |

## 16. Critérios de Aceite (Gherkin)

```gherkin
Funcionalidade: Auditoria transversal imutável e observabilidade ponta a ponta

  Cenário: Evento fora do esquema comum é rejeitado
    Dado um evento de auditoria publicado por uma feature de origem em formato inválido
    Quando o validador de esquema processa o evento
    Então o evento deve ser rejeitado
    E um alerta técnico deve ser gerado para a feature de origem

  Cenário: Registro existente nunca é alterado
    Dado um evento de auditoria já armazenado
    Quando uma correção é necessária
    Então um novo evento de retificação deve ser criado, referenciando o original
    E o evento original nunca deve ser alterado ou removido

  Cenário: Trilha consolidada reconstitui a jornada completa
    Dado um sinistro que passou pelas 8 etapas do pipeline
    Quando compliance consulta a trilha consolidada
    Então todas as 8 etapas devem ser exibidas em ordem cronológica

  Cenário: Etapa pulada por fail-open é registrada como "não aplicável", sem gerar alerta
    Dado um sinistro que passou por fail-open na feature de score
    Quando compliance consulta a trilha consolidada
    Então a etapa correspondente deve ser exibida como "não aplicável (n/a)"
    E nenhum alerta operacional deve ser disparado por essa ausência

  Cenário: Lacuna inesperada é exibida explicitamente e gera alerta
    Dado um sinistro com uma etapa ausente sem nenhuma explicação registrada
    Quando compliance consulta a trilha consolidada
    Então a etapa ausente deve ser exibida explicitamente como lacuna inesperada
    E um alerta operacional deve ter sido disparado no momento da detecção
    E nenhum dado deve ser inferido para preenchê-la

  Cenário: Acesso ao painel de compliance é segregado do painel do analista
    Dado um usuário autenticado como analista
    Quando esse usuário tenta acessar o painel de compliance
    Então o acesso deve ser negado, mantendo a segregação definida

  Cenário: Observabilidade mede a latência ponta a ponta
    Dado um sinistro processado por todas as etapas do pipeline
    Quando a métrica de latência total é calculada
    Então ela deve refletir o tempo desde a ingestão até a geração do alerta
```

## 17. KPIs

- Latência ponta a ponta (p50, p95, p99) do pipeline completo.
- Disponibilidade de cada dependência crítica (serviço de score, repositório de imagens, base de apólices, histórico de sinistros).
- Taxa de sinistros com trilha completa (sem lacunas inesperadas — etapas "n/a" de fail-open não contam como incompletude) vs. com lacuna inesperada.
- Volume de eventos de auditoria por período (para dimensionamento de armazenamento).
- Tempo de resposta das consultas de trilha consolidada no painel de compliance.

## 18. Riscos

| Risco | Mitigação |
|---|---|
| Volume de eventos crescer mais rápido que o previsto, pressionando custo de armazenamento | Escalabilidade horizontal desde o design; políticas de retenção (ver Dependências) para conter crescimento indefinido |
| Divergência de esquema entre features ao longo do tempo (nova feature futura não segue o padrão) | Validação de esquema obrigatória e centralizada nesta feature, não opcional |
| Painel de compliance expor mais dado do que o necessário para a função de auditoria | Revisão periódica de quais campos são exibidos vs. apenas armazenados |

## 19. Dependências

- Validação jurídica/DPO do prazo de retenção inicial de 2 anos, antes de produção.
- Escolha da tecnologia de armazenamento append-only (ex.: event store dedicado, ou tabela com trigger de bloqueio de update/delete em banco relacional).
- Ferramenta de observabilidade/dashboards já em uso na ACME, para reaproveitamento em vez de nova adoção.

## 20. Itens Fora do Escopo (desta feature)

- Cálculo de qualquer sinal, score, faixa ou decisão de roteamento (features 2.1–2.8).
- UI do painel do analista (feature 2.7, já define seu próprio acesso).
- Ação corretiva automática a partir de um alerta operacional (sempre humana).
- Anonimização específica para fins de calibração do motor (já tratada na feature 2.8 — esta feature apenas armazena o que cada uma decide registrar).

## 21. Roadmap Futuro

1. Anonimização ou expurgo automático de eventos após expiração do prazo de retenção definido.
2. Exportação formal da trilha consolidada para atender auditorias externas/regulatórias, em formato padronizado.
3. Detecção automática de anomalias na própria trilha de auditoria (ex.: padrão de lacunas incomum, possível indicativo de falha sistêmica não percebida).

## 22. Glossário

| Termo | Definição |
|---|---|
| **Append-only** | Modelo de armazenamento em que registros só podem ser adicionados, nunca alterados ou removidos. |
| **Trilha consolidada** | Reconstituição da jornada completa de um sinistro através das 8 etapas do pipeline, ordenada por timestamp. |
| **Não aplicável (n/a)** | Registro de que uma etapa foi pulada por um caminho já documentado pela própria feature de origem (fail-open, anomalia técnica) — não é tratado como problema nem gera alerta. |
| **Lacuna** | Ausência real e inexplicada de uma etapa na trilha de um sinistro — sinal de evento perdido ou falha de infraestrutura, exibida explicitamente e nunca inferida; gera alerta operacional. |
| **Retificação** | Novo evento de auditoria que corrige um registro anterior, referenciando-o, sem alterá-lo. |
| **Observabilidade ponta a ponta** | Medição de latência, disponibilidade e saúde operacional ao longo de todo o pipeline, não apenas de uma etapa isolada. |
| **Painel de compliance** | Interface segregada do painel do analista, com acesso à trilha completa e métricas agregadas de todas as features. |
