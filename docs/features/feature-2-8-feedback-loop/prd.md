# PRD — Motor Antifraude de Sinistros
## Feature 2.8: Feedback Loop
**ACME Seguros — Seguro de Celular — Trilha B (sinistro por foto)**

---

## 1. Visão Geral

Esta feature fecha o ciclo do motor: coleta as decisões terminais registradas pelo analista (feature 2.7), transforma-as em **métricas agregadas e anonimizadas de concordância** entre o que o motor sinalizou (score/faixa) e o que o analista decidiu, e disponibiliza esse insumo para a **calibração da equipe antifraude** — a mesma governança de revisão trimestral e gatilho reativo já estabelecida nas features 2.3, 2.4 e 2.5.

> **Nota de fronteira importante:** nesta fase do produto **não existe modelo de ML** (decisão fechada na feature 2.3 — apenas regra determinística). Por isso, "feedback loop" aqui significa **insumo agregado para recalibração manual de pesos e limiares pela equipe antifraude**, não retreinamento automático de um modelo. O pipeline de retreinamento de ML propriamente dito — mencionado no catálogo original do produto — fica no roadmap, para quando um modelo for introduzido (também já sinalizado como roadmap na feature 2.3).

Esta feature **não** decide sozinha nenhuma mudança de peso/limiar — ela apenas fornece o insumo agregado; a decisão de calibrar continua sendo humana, da equipe antifraude, sob a mesma governança já estabelecida.

## 2. Problema

Sem um mecanismo formal de agregação do feedback, as decisões dos analistas ficariam dispersas nos registros de auditoria individuais (feature 2.7), exigindo trabalho manual pesado para a equipe antifraude extrair qualquer sinal de calibração — e criando risco real de a calibração nunca acontecer na prática, mesmo com a governança formalmente definida.

## 3. Objetivos

- Consumir as decisões terminais do analista (feature 2.7) e classificá-las como concordância ou discordância com o score/faixa sinalizado.
- Agregar essas classificações de forma **anonimizada**, sem re-expor dados pessoais individuais do sinistro.
- Calcular as métricas de calibração já previstas nas features anteriores (taxa de falso positivo/negativo, taxa de concordância analista×motor).
- Disponibilizar essas métricas de forma contínua para consulta da equipe antifraude, respeitando o cronograma de decisão já estabelecido (revisão trimestral + gatilho reativo).
- Preservar a mesma base legal (legítimo interesse / prevenção à fraude) sem introduzir novo tratamento de dados pessoais.

**Não-objetivos desta feature:** decidir ou aplicar mudanças de peso/limiar (isso é decisão humana da equipe antifraude, já governada nas features 2.3/2.4/2.5), treinar ou retreinar um modelo de ML (não existe nesta fase), julgar o mérito do sinistro.

## 4. Personas

| Persona | Papel nesta feature |
|---|---|
| **Feature 2.7 (Painel do Analista)** | Produtora das decisões terminais que alimentam esta feature. |
| **Equipe antifraude** | Consumidora das métricas agregadas, para calibração de pesos/limiares (revisão trimestral + gatilho reativo). |
| **Compliance** | Audita se a agregação está de fato anonimizada e sem novo tratamento de dados pessoais. |

## 5. Jornada (Equipe Antifraude)

1. Analistas tomam decisões terminais no painel (feature 2.7): aprovar análise reforçada, encaminhar, ou marcar falso positivo (o "pedir documentos" é intermediário — ver Regras de Negócio).
2. Esta feature classifica cada decisão terminal como concordância ou discordância com a faixa/score sinalizado, e agrega essas classificações de forma anonimizada.
3. Equipe antifraude consulta as métricas agregadas continuamente, e usa esse insumo na revisão trimestral ou quando o gatilho reativo (taxa fora da banda) dispara.
4. Se decidir recalibrar pesos ou limiares, a equipe segue o processo já estabelecido (mudança versionada, auditável) nas features 2.3/2.4/2.5 — esta feature não participa dessa mudança, apenas do insumo que a motivou.

## 6. Fluxo Completo (com casos intermediários e sem visibilidade de destino externo)

```
[Decisão do analista] (Feature 2.7)
        │
        ▼
┌─────────────────────────────┐
│ Ação é terminal ou               │
│ intermediária?                    │
└───────┬───────────────┬─────┘
   terminal │           │ intermediária ("pedir documentos")
      (aprovar,          │
     encaminhar,         ▼
   falso positivo)  ┌───────────────────────────┐
       │             │ Não gera sinal de feedback   │
       ▼             │ ainda — aguarda decisão       │
┌─────────────────┐ │ terminal futura sobre o        │
│ Classificador de   │ │ mesmo caso                     │
│ Concordância        │ └───────────────────────────┘
│ (concorda / discorda│
│ com faixa/score)    │
└─────────┬───────────┘
          ▼
┌─────────────────────────────┐
│ Anonimização                    │
│ (remove ID do sinistro e         │
│ qualquer dado pessoal; mantém    │
│ apenas score, faixa, sinais      │
│ ativos, classificação de         │
│ concordância)                    │
└───────────────┬───────────────────┘
                ▼
┌─────────────────────────────┐
│ Agregador de Métricas            │
│ (taxa de falso positivo/negativo,│
│ taxa de concordância)            │
└───────────────┬───────────────────┘
                ▼
┌─────────────────────────────┐
│ Registro de Auditoria            │
│ (agregação, não dado individual) │
└───────────────┬───────────────────┘
                ▼
   Consulta pela Equipe Antifraude
   (revisão trimestral + gatilho reativo)
```

**Ponto crítico do guardrail:** a agregação **nunca** expõe o sinistro individual à equipe antifraude nesta etapa — apenas o dado anonimizado entra no cálculo das métricas.

## 7. Regras de Negócio

1. Apenas **decisões terminais** geram sinal de feedback: **aprovar análise reforçada** e **encaminhar** contam como **concordância** com a faixa/score sinalizado; **marcar falso positivo** conta como **discordância**.
2. **Pedir documentos** é uma ação intermediária — não gera sinal de feedback no momento em que é tomada. O sinal só é gerado quando o caso retornar à fila e receber uma das 3 ações terminais.
3. Casos encaminhados para Compliance ou Investigação Especializada contam como concordância no momento do encaminhamento (o analista concordou que o caso merecia atenção). Esta feature **não tem visibilidade** sobre a resolução final desses casos pelos times de destino nesta fase — isso é uma fronteira explícita de integração (ver Dependências).
4. Toda agregação é feita de forma **anonimizada**: o dado individual usado no cálculo mantém apenas score, faixa, sinais ativos/indisponíveis e a classificação de concordância/discordância — nunca o ID do sinistro, nem qualquer dado pessoal do cliente.
5. As métricas agregadas são atualizadas em **batch a cada hora**, para que estejam disponíveis quando a equipe antifraude precisar consultar — mas a decisão de recalibrar segue o cronograma já estabelecido (revisão trimestral + gatilho reativo), não é automática.
6. Uma métrica só é apresentada como confiável quando o período de agregação tiver **pelo menos 10 decisões terminais**; abaixo disso, é marcada como "amostra insuficiente" em vez de exibida como taxa percentual normal.
7. Esta feature não aplica nenhuma mudança de peso/limiar por conta própria — apenas fornece o insumo agregado. A mudança em si é feita pela equipe antifraude através do mecanismo de configuração versionada já definido nas features 2.3/2.4/2.5.
8. A base legal do tratamento nesta etapa é a mesma finalidade original (legítimo interesse / prevenção à fraude) já documentada — a anonimização evita configurar uma nova finalidade de tratamento de dados pessoais.

## 8. Arquitetura de Alto Nível

```
┌─────────────────────────┐
│ Feature 2.7                │
│ (Painel do Analista)       │
└────────────┬──────────────┘
             │ decisão (ação + faixa/score do caso)
             ▼
┌──────────────────────────────────────┐
│ FEATURE 2.8 — Feedback Loop              │
│                                          │
│  ┌───────────────────────────┐         │
│  │ Filtro de Decisão Terminal    │         │
│  └─────────────┬─────────────────┘      │
│                ▼                        │
│  ┌───────────────────────────┐         │
│  │ Classificador de Concordância │        │
│  └─────────────┬─────────────────┘      │
│                ▼                        │
│  ┌───────────────────────────┐         │
│  │ Anonimizador                   │        │
│  └─────────────┬─────────────────┘      │
│                ▼                        │
│  ┌───────────────────────────┐         │
│  │ Agregador de Métricas           │        │
│  └─────────────┬─────────────────┘      │
│                ▼                        │
│  ┌───────────────────────────┐         │
│  │ Publicador de Auditoria         │────────┼──▶ Log imutável (agregado)
│  └─────────────┬─────────────────┘      │
└────────────────┼─────────────────────────┘
                 ▼
      Painel de Métricas de Calibração
      (consultado pela Equipe Antifraude)
                 │
                 ▼
      Mudança de configuração versionada
      (features 2.3 / 2.4 / 2.5 — processo já existente)
```

## 9. Requisitos Funcionais

| ID | Requisito |
|---|---|
| RF01 | O sistema deve consumir decisões registradas pela feature 2.7 e identificar se são terminais (aprovar análise reforçada, encaminhar, falso positivo) ou intermediárias (pedir documentos). |
| RF02 | O sistema deve classificar decisões terminais como concordância ou discordância com a faixa/score sinalizado. |
| RF03 | O sistema deve anonimizar o dado antes de agregá-lo, removendo ID do sinistro e qualquer dado pessoal, mantendo apenas score, faixa, sinais e classificação de concordância. |
| RF04 | O sistema deve calcular, em batch a cada hora, as métricas de taxa de falso positivo, taxa de falso negativo (quando aplicável) e taxa de concordância analista×motor. |
| RF05 | O sistema não deve aplicar nenhuma mudança de peso ou limiar por conta própria — apenas disponibilizar o insumo agregado. |
| RF06 | O sistema deve tratar casos "pedir documentos" como sem sinal de feedback até que uma decisão terminal seja registrada para o mesmo caso. |
| RF07 | O sistema deve registrar de forma auditável a agregação (não o dado individual) usada em cada cálculo de métrica. |
| RF08 | O sistema deve marcar uma métrica como "amostra insuficiente" quando o período de agregação tiver menos de 10 decisões terminais. |

## 10. Requisitos Não Funcionais

- **Anonimização por padrão:** nenhum dado pessoal individual entra na agregação (requisito de privacidade, não apenas de segurança).
- **Atualização em batch:** métricas recalculadas a cada hora, refletindo decisões recentes sem exigir processamento manual.
- **Auditabilidade:** toda agregação usada para embasar uma decisão de calibração é rastreável (mesmo sendo dado agregado, não individual).
- **Consistência com a governança já estabelecida:** esta feature não introduz um novo cronograma de decisão — usa o mesmo já definido (revisão trimestral + gatilho reativo).

## 11. Integrações

- **Feature 2.7 (Painel do Analista)** — fonte das decisões terminais e intermediárias.
- **Serviço de Configuração** — consumido indiretamente: as métricas geradas aqui embasam mudanças que são aplicadas através do mesmo serviço já usado pelas features 2.3/2.4/2.5 (esta feature não escreve diretamente nele).
- **Painel de Métricas de Calibração** — consumidor das métricas agregadas pela equipe antifraude (pode ser uma extensão do painel de compliance já previsto no roadmap de outras features).
- **Log/Auditoria** — armazenamento imutável da agregação.

## 12. Segurança e LGPD

- Esta feature é, por definição, a camada que garante que o feedback do analista **não configura uma nova finalidade de tratamento de dados pessoais** — a anonimização antes da agregação é o mecanismo que sustenta essa garantia, já acordada na decisão original sobre a base legal do feedback loop.
- Nenhum dado pessoal do cliente, nem o ID do sinistro, é retido na camada de métricas agregadas.
- Acesso ao painel de métricas de calibração é restrito à equipe antifraude e compliance.

## 13. Auditoria

Para cada atualização de métrica agregada, registrar de forma imutável:
- Período de referência da agregação.
- Volume de decisões terminais consideradas (por tipo: concordância/discordância).
- Valor calculado de cada métrica (taxa de falso positivo, falso negativo, concordância).
- Timestamp da atualização.

**Não** registrar, nesta camada, o ID do sinistro individual ou qualquer dado pessoal — isso permanece apenas no registro de auditoria da decisão original (feature 2.7), sob os controles de acesso já vigentes lá.

## 14. Casos de Uso

1. **Analista aprova análise reforçada:** decisão terminal → classificada como concordância, entra na agregação anonimizada.
2. **Analista marca falso positivo:** decisão terminal → classificada como discordância, entra na agregação anonimizada.
3. **Analista pede documentos, depois marca falso positivo quando a documentação retorna:** apenas a segunda decisão (terminal) gera sinal de feedback.
4. **Analista encaminha para Compliance:** classificado como concordância no momento do encaminhamento; sem visibilidade da resolução final do time de destino nesta fase.
5. **Equipe antifraude consulta métricas na revisão trimestral:** vê a taxa de concordância agregada do período, sem acesso a nenhum caso individual através desta feature.

## 15. Casos de Exceção

| Exceção | Tratamento |
|---|---|
| Caso "pedir documentos" nunca recebe decisão terminal (abandono) | Não gera sinal de feedback; fica fora da agregação (não é tratado como discordância nem concordância por padrão). |
| Feature 2.7 indisponível ao publicar uma decisão | Sinal de feedback correspondente fica pendente; reconciliação ocorre quando a decisão for processada normalmente — nenhuma métrica é calculada com dado incompleto. |
| Volume de decisões insuficiente no período (menos de 10 decisões terminais) | Métrica é marcada como "amostra insuficiente" em vez de exibida como taxa percentual normal. |

## 16. Critérios de Aceite (Gherkin)

```gherkin
Funcionalidade: Agregação anonimizada de feedback para calibração

  Cenário: Decisão terminal de aprovação gera sinal de concordância
    Dado que o analista registra a ação "aprovar análise reforçada" para um caso de faixa alta
    Quando a feature de feedback loop processa a decisão
    Então o sinal deve ser classificado como concordância
    E deve entrar na agregação anonimizada

  Cenário: Marcar falso positivo gera sinal de discordância
    Dado que o analista registra a ação "marcar falso positivo" para um caso de faixa alta
    Quando a feature de feedback loop processa a decisão
    Então o sinal deve ser classificado como discordância

  Cenário: Pedir documentos não gera sinal de feedback imediato
    Dado que o analista registra a ação "pedir documentos"
    Quando a feature de feedback loop processa a decisão
    Então nenhum sinal de concordância ou discordância deve ser gerado para este caso ainda

  Cenário: Agregação nunca expõe o ID do sinistro
    Dado qualquer conjunto de decisões terminais processadas
    Quando a agregação é calculada
    Então o resultado agregado não deve conter o ID do sinistro nem dados pessoais do cliente

  Cenário: Métricas são recalculadas em batch a cada hora
    Dado um conjunto de decisões terminais registradas ao longo da última hora
    Quando o batch de agregação é executado
    Então os valores das métricas devem refletir as decisões processadas até aquele momento

  Cenário: Amostra insuficiente é marcada em vez de exibida como taxa confiável
    Dado um período de agregação com menos de 10 decisões terminais
    Quando a métrica é calculada
    Então o sistema deve marcá-la como "amostra insuficiente" em vez de exibir uma taxa percentual normal

  Cenário: Auditoria registra a agregação, não o dado individual
    Dado uma atualização de métrica agregada
    Quando o registro de auditoria é gerado
    Então ele deve conter o volume e os valores agregados
    E não deve conter o ID do sinistro individual
```

## 17. KPIs

- Taxa de concordância analista×motor (% de decisões terminais que confirmam a faixa/score sinalizado).
- Taxa de falso positivo (proporção de casos sinalizados que o analista marcou como falso positivo).
- Volume de decisões terminais vs. intermediárias por período (indicador de quantos casos ficam "pendentes de resolução").
- Tamanho da amostra por período de agregação (para contextualizar a confiabilidade das taxas).

## 18. Riscos

| Risco | Mitigação |
|---|---|
| Baixo volume de decisões terminais em períodos curtos gera métricas pouco confiáveis | Marcação automática de "amostra insuficiente" abaixo de 10 decisões terminais no período |
| Encaminhamentos para Compliance/Investigação Especializada são tratados como concordância mesmo que a investigação depois conclua diferente | Fronteira de integração explícita nesta fase (sem visibilidade do resultado); revisitar no roadmap quando houver retorno desses times |
| Equipe antifraude interpretar a taxa agregada como decisão automática de calibração | Reforçar, via processo e documentação, que a decisão de mudança continua humana e segue a governança já estabelecida |

## 19. Dependências

- Canal de consulta (painel de métricas de calibração) para a equipe antifraude acessar os dados agregados desta feature — pode reaproveitar ou estender o painel de compliance já previsto no roadmap de outras features.
- Definição de como (ou se) incorporar, no futuro, o retorno de resolução dos casos encaminhados para Compliance/Investigação Especializada à métrica de concordância — não fechado nesta PRD.

## 20. Itens Fora do Escopo (desta feature)

- Decisão ou aplicação de mudanças de peso/limiar (permanece na equipe antifraude, via processo já estabelecido nas features 2.3/2.4/2.5).
- Retreinamento de modelo de ML (não existe modelo nesta fase — roadmap).
- Resolução final de casos encaminhados para Compliance ou Investigação Especializada (fora da visibilidade desta feature nesta fase).
- UI do painel de métricas de calibração (pode ser feature futura própria ou extensão de painel já previsto).

## 21. Roadmap Futuro

1. Incorporar o retorno de resolução dos casos encaminhados para Compliance/Investigação Especializada à métrica de concordância, fechando essa fronteira de visibilidade.
2. Pipeline de retreinamento automático quando um modelo de ML for introduzido (consistente com o roadmap já sinalizado na feature 2.3), com a mesma garantia de anonimização estabelecida aqui.
3. Alertas automáticos de gatilho reativo quando a taxa de falso positivo/negativo saltar fora da banda esperada, notificando a equipe antifraude proativamente em vez de depender de consulta manual.

## 22. Glossário

| Termo | Definição |
|---|---|
| **Decisão terminal** | Ação do analista que conclui o fluxo antifraude do caso (aprovar análise reforçada, encaminhar, marcar falso positivo). |
| **Decisão intermediária** | Ação que não conclui o fluxo (pedir documentos) — o caso retorna à fila para uma decisão terminal futura. |
| **Concordância** | Classificação de uma decisão terminal que confirma que o caso merecia a atenção sinalizada pelo score/faixa. |
| **Discordância** | Classificação de uma decisão terminal (marcar falso positivo) que indica que os indícios sinalizados não se sustentaram. |
| **Anonimização** | Remoção de qualquer identificador pessoal ou de sinistro antes da agregação, garantindo que a mesma finalidade original de tratamento de dados seja preservada. |
