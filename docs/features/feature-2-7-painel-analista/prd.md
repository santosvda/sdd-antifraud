# PRD — Motor Antifraude de Sinistros
## Feature 2.7: Painel do Analista
**ACME Seguros — Seguro de Celular — Trilha B (sinistro por foto)**

---

## 1. Visão Geral

Esta feature é a **interface humana** do motor antifraude: o painel onde o analista vê a fila priorizada por risco, abre o cartão completo de cada caso (montado pela feature 2.6) e registra sua decisão. É aqui que o princípio de **human-in-the-loop obrigatório** deixa de ser um conceito de arquitetura e se torna a experiência real de trabalho do analista.

Esta feature **não** decide nada por conta própria — ela apresenta o que as features anteriores já calcularam e captura a decisão humana. As quatro ações disponíveis ao analista (aprovar análise reforçada, pedir documentos, encaminhar, marcar falso positivo) são todas sobre **o fluxo de investigação antifraude**, nunca sobre o mérito do sinistro (cobertura, indenização) — essa distinção é a linha vermelha estrutural desta feature.

## 2. Problema

Sem um painel dedicado, o analista dependeria de telas genéricas do sistema de sinistros (sem priorização por risco, sem indícios organizados, sem justificativa pronta) para fazer o trabalho de triagem antifraude — aumentando o tempo de análise, o risco de perder casos de alto risco em meio ao volume normal, e a inconsistência na forma como as decisões são registradas (ou não registradas) para auditoria e para o feedback loop.

## 3. Objetivos

- Exibir a fila de casos priorizada por risco, respeitando o roteamento e a sugestão de prioridade já calculados (features 2.5 e 2.6).
- Exibir o cartão completo do caso — indícios, evidências, score, faixa, justificativa, correlação — exatamente como montado pela feature 2.6, sem reprocessamento.
- Capturar a decisão do analista através de ações estruturadas, sempre com justificativa registrada.
- Garantir que nenhuma ação disponível no painel decida o mérito do sinistro — todas as ações são sobre o fluxo antifraude.
- Oferecer atalho rápido para o histórico completo do cliente/aparelho, além dos sinistros correlacionados já trazidos no alerta.
- Manter a interface livre de linguagem acusatória em qualquer elemento visual ou textual.

**Não-objetivos desta feature:** decidir cobertura/indenização do sinistro, calcular score/faixa/roteamento (features anteriores), persistir o feedback para retreinamento do modelo (feature 2.8), notificar o cliente final diretamente.

## 4. Personas

| Persona | Papel nesta feature |
|---|---|
| **Analista de sinistro** | Usuário primário — opera o painel, abre casos, registra decisões. |
| **Feature 2.5 / 2.6** | Fontes de dados consumidas pelo painel (fila e alerta completo). |
| **Sistema de Sinistros** | Recebe comandos derivados de algumas ações do analista (ex.: pedido de documentos), sem que o painel decida o mérito. |
| **Feature 2.8 (Feedback Loop, futura)** | Consumidora da decisão registrada aqui (fora do escopo desta feature implementar o retreinamento em si). |
| **Compliance** | Audita as decisões registradas e a ausência de linguagem acusatória na interface. |

## 5. Jornada do Usuário (Analista)

1. Analista abre o painel e vê a fila priorizada — normal, reforçada e revisão manual, cada uma ordenada pela sugestão de prioridade (ou, na fila de revisão manual, sem ordenação por risco, já que não há score).
2. Analista seleciona um caso e abre o cartão completo: indícios com evidência, score e faixa (ou aviso de "sem classificação"), justificativa legível, sinistros correlacionados, sugestão de prioridade.
3. Analista usa os atalhos de histórico do cliente/aparelho quando quer investigar mais a fundo do que os 3 sinistros correlacionados já trazidos no alerta.
4. Analista decide uma das quatro ações disponíveis, preenchendo a justificativa da decisão.
5. A decisão é registrada de forma auditável e o caso sai da fila ativa do analista (ou é reencaminhado, conforme a ação escolhida).

## 6. Fluxo Completo (com as 4 ações do analista)

```
[Fila priorizada] (Feature 2.5 + 2.6)
        │
        ▼
┌─────────────────────────────┐
│ Analista abre o cartão do caso │
│ (conteúdo da Feature 2.6)      │
└───────────────┬────────────────┘
                ▼
┌─────────────────────────────┐
│ Analista escolhe uma ação:     │
└───────────────┬────────────────┘
                │
    ┌───────────┼───────────┬───────────────┐
    ▼           ▼           ▼               ▼
┌─────────┐ ┌─────────┐ ┌─────────┐ ┌───────────────┐
│Aprovar    │ │Pedir       │ │Encaminhar  │ │Marcar falso     │
│análise    │ │documentos  │ │(Compliance │ │positivo          │
│reforçada  │ │→ estado    │ │ou Investig.│ │                  │
│           │ │"aguardando"│ │Especializ.)│ │                  │
└────┬─────┘ └────┬─────┘ └────┬─────┘ └────┬───────────────┘
     │            │            │            │
     ▼            ▼            ▼            ▼
┌─────────────────────────────────────────────────────┐
│ Justificativa obrigatória (texto livre) para           │
│ qualquer uma das 4 ações                                │
└───────────────────────┬─────────────────────────────────┘
                        ▼
          ┌─────────────────────────────┐
          │ Registro de Auditoria          │
          │ (ação, justificativa,          │
          │ analista, timestamp)           │
          └───────────────┬───────────────────┘
                          ▼
       ┌──────────────────┼──────────────────────┐
       ▼                  ▼                       ▼
┌─────────────┐ ┌───────────────────┐ ┌───────────────────────┐
│ Comando ao     │ │ Caso reencaminhado  │ │ Evento para Feature      │
│ Sistema de       │ │ para outra fila/     │ │ 2.8 (Feedback Loop,       │
│ Sinistros         │ │ time (se aplicável)  │ │ futura — fora de escopo) │
│ (ex.: pedir docs) │ │                     │ │                          │
└─────────────┘ └───────────────────┘ └───────────────────────┘
```

**Ponto crítico do guardrail:** nenhuma das 4 ações aprova, nega ou decide a cobertura/indenização do sinistro — todas operam exclusivamente sobre o fluxo de investigação antifraude.

## 7. Regras de Negócio

1. A fila exibida respeita o roteamento definido pela feature 2.5 (normal, reforçada, revisão manual) e a ordenação por sugestão de prioridade definida pela feature 2.6.
2. O cartão do caso exibe o conteúdo da feature 2.6 **sem reprocessamento** — o painel é uma camada de apresentação, não de cálculo.
3. As únicas ações disponíveis ao analista são: **aprovar análise reforçada** (confirma que o caso deve seguir/permanecer em investigação antifraude mais profunda), **pedir documentos** (aciona, via Sistema de Sinistros, uma solicitação de documentação adicional ao cliente; o caso entra em estado **"aguardando documentos"**, exibido em seção separada da fila, e retorna automaticamente à fila ativa quando o Sistema de Sinistros sinalizar o recebimento da documentação), **encaminhar** (move o caso para um dos 2 destinos disponíveis — **Compliance** ou **Investigação Especializada**) e **marcar falso positivo** (registra que os indícios não configuram suspeita, para fins de feedback).
4. **Nenhuma dessas ações decide o mérito do sinistro** (cobertura, indenização) — essa decisão permanece exclusivamente no Sistema de Sinistros, fora deste motor.
5. Toda ação exige **justificativa em texto livre**, obrigatória, antes de ser confirmada.
6. Nenhum elemento da interface (rótulos, textos de indício, textos de ação) usa linguagem acusatória — segue o mesmo padrão de linguagem de indício já estabelecido nas features anteriores.
7. O atalho de histórico do cliente/aparelho é uma consulta separada da lista de correlação já trazida no alerta (limitada a 3 sinistros) — o atalho permite ver o histórico completo, sob os mesmos controles de acesso já vigentes no sistema de sinistros.
8. Toda decisão do analista é registrada de forma imutável, com identificação do analista, timestamp, ação escolhida e justificativa — esse registro é o insumo para a feature 2.8 (Feedback Loop), mesmo que a persistência para retreinamento seja implementada por aquela feature, não por esta.

## 8. Arquitetura de Alto Nível

```
┌─────────────────┐ ┌─────────────────┐
│ Feature 2.5        │ │ Feature 2.6        │
│ (fila/roteamento)  │ │ (alerta completo)  │
└────────┬────────────┘ └────────┬────────────┘
         └─────────────┬─────────┘
                       ▼
        ┌──────────────────────────────────┐
        │ FEATURE 2.7 — Painel do Analista     │
        │                                      │
        │  ┌───────────────────────────┐     │
        │  │ Listagem de Fila Priorizada   │    │
        │  └─────────────┬─────────────────┘  │
        │                ▼                    │
        │  ┌───────────────────────────┐     │
        │  │ Cartão do Caso                │    │
        │  │ (renderiza dados da 2.6)      │    │
        │  └─────────────┬─────────────────┘  │
        │                ▼                    │
        │  ┌───────────────────────────┐     │
        │  │ Captura de Ação + Justificativa│    │
        │  └─────────────┬─────────────────┘  │
        │                ▼                    │
        │  ┌───────────────────────────┐     │
        │  │ Publicador de Auditoria        │────┼──▶ Log imutável
        │  └─────────────┬─────────────────┘  │
        │                ▼                    │
        │  ┌───────────────────────────┐     │
        │  │ Atalho de Histórico            │◀───┼── Base de Sinistros/Apólices
        │  │ Cliente/Aparelho               │    │
        │  └───────────────────────────┘     │
        └────────────────┼─────────────────────┘
                         ▼
      ┌─────────────────────┐ ┌───────────────────────────┐
      │ Sistema de Sinistros  │ │ Feature 2.8 (Feedback Loop, │
      │ (comando de pedir      │ │ futura)                     │
      │ documentos, etc.)      │ │                             │
      └─────────────────────┘ └───────────────────────────┘
```

## 9. Requisitos Funcionais

| ID | Requisito |
|---|---|
| RF01 | O sistema deve exibir a fila de casos priorizada por risco, respeitando o roteamento da feature 2.5 e a sugestão de prioridade da feature 2.6. |
| RF02 | O sistema deve exibir o cartão completo do caso conforme montado pela feature 2.6, sem reprocessamento. |
| RF03 | O sistema deve oferecer exatamente 4 ações ao analista: aprovar análise reforçada, pedir documentos (com transição para estado "aguardando documentos"), encaminhar (para Compliance ou Investigação Especializada), marcar falso positivo. |
| RF04 | O sistema deve exigir justificativa em texto livre, obrigatória, para qualquer ação antes de confirmá-la. |
| RF05 | O sistema não deve oferecer nenhuma ação que decida o mérito do sinistro (cobertura, indenização). |
| RF06 | O sistema deve oferecer um atalho para consulta do histórico completo do cliente/aparelho, distinto da correlação limitada do alerta. |
| RF07 | O sistema deve registrar de forma imutável cada decisão do analista: ação, justificativa, identificação do analista e timestamp. |
| RF08 | Nenhum elemento textual da interface deve usar linguagem acusatória. |
| RF09 | O sistema deve exibir casos em estado "aguardando documentos" em seção separada da fila ativa, e retorná-los automaticamente à fila quando o Sistema de Sinistros sinalizar o recebimento da documentação. |

## 10. Requisitos Não Funcionais

- **Usabilidade:** cartão do caso deve ser legível sem necessidade de treinamento extenso — informação organizada na mesma estrutura da feature 2.6.
- **Auditabilidade:** toda decisão gera registro imutável.
- **Controle de acesso:** painel restrito a analistas autenticados, com segregação de acesso analista vs. compliance já estabelecida.
- **Disponibilidade:** o painel deve refletir o estado mais atual da fila (roteamento e prioridade) sem exigir atualização manual constante pelo analista.
- **Consistência:** o cartão do caso exibido no painel deve ser idêntico ao conteúdo produzido pela feature 2.6, sem divergência de apresentação.

## 11. Integrações

- **Feature 2.5 (Roteamento)** — fonte da fila e do estado de cada caso.
- **Feature 2.6 (Geração de Alertas)** — fonte do conteúdo do cartão do caso.
- **Sistema de Sinistros** — recebe comandos derivados de ações do analista (ex.: solicitação de documentos), e é a fonte do histórico completo de cliente/aparelho para o atalho.
- **Log/Auditoria** — armazenamento imutável de cada decisão.
- **Feature 2.8 (Feedback Loop, futura)** — consumidora do registro de decisão (fora do escopo desta feature implementar a persistência para retreinamento).

## 12. Segurança e LGPD

- Acesso ao painel restrito a analistas autenticados, com controle de acesso e segregação analista vs. compliance já estabelecidos nas features anteriores.
- O atalho de histórico do cliente/aparelho segue os mesmos controles de acesso e minimização já vigentes no sistema de sinistros — o painel não duplica nem armazena esses dados além do necessário para exibição.
- Toda justificativa de texto livre registrada pelo analista deve evitar a inclusão de dados pessoais sensíveis desnecessários — orientação de uso a ser comunicada à equipe (não é uma validação técnica automática nesta fase).
- Base legal: legítimo interesse / prevenção à fraude, documentada — mesma base das demais features do motor.

## 13. Auditoria

Para cada decisão registrada, armazenar de forma imutável:
- ID do sinistro, identificação do analista responsável.
- Ação escolhida (uma das 4) e justificativa em texto livre.
- Timestamp da decisão.
- Estado do cartão do caso no momento da decisão (score, faixa, indícios), para permitir reconstituir o contexto exato em que a decisão foi tomada.

## 14. Casos de Uso

1. **Analista aprova análise reforçada:** caso de faixa alta com indícios claros → analista confirma que o caso segue em investigação antifraude mais profunda, com justificativa.
2. **Analista pede documentos:** caso com indícios ambíguos → analista solicita documentação adicional ao cliente via Sistema de Sinistros, com justificativa; caso passa para o estado "aguardando documentos", visível em seção separada, e retorna à fila ativa quando a documentação chega.
3. **Analista encaminha o caso:** indícios sugerem padrão que exige investigação especializada → analista encaminha para Compliance ou Investigação Especializada, com justificativa.
4. **Analista marca falso positivo:** indícios calculados não se sustentam após análise humana → analista registra falso positivo, com justificativa, alimentando o futuro feedback loop.
5. **Analista consulta histórico completo:** caso traz apenas 3 sinistros correlacionados no alerta, mas o analista quer ver o histórico completo do aparelho → usa o atalho, acessando a base de sinistros diretamente.

## 15. Casos de Exceção

| Exceção | Tratamento |
|---|---|
| Analista tenta confirmar uma ação sem preencher justificativa | Ação bloqueada até que a justificativa seja preenchida — este é o único bloqueio aceitável no painel, pois é sobre o registro da decisão, não sobre o sinistro em si. |
| Sistema de Sinistros indisponível ao processar comando de "pedir documentos" | Ação do analista é registrada normalmente na auditoria; comando ao Sistema de Sinistros é reenviado com retry; alerta técnico se persistir. O caso entra em "aguardando documentos" independentemente do resultado do comando, para não perder o registro da decisão. |
| Caso em "aguardando documentos" não recebe retorno do cliente por período prolongado | Fora do escopo desta feature definir prazo/escalonamento (ver Roadmap) — nesta fase, o caso permanece em "aguardando documentos" até sinalização do Sistema de Sinistros. |
| Caso "sem classificação" (herdado do fail-open) é aberto pelo analista | Cartão exibe claramente a ausência de score/faixa (conforme já definido na feature 2.6); todas as 4 ações continuam disponíveis normalmente. |
| Consulta ao histórico completo do cliente/aparelho falha (base indisponível) | Atalho exibe erro claro ao analista, sem impedir as demais funções do painel nem o registro de decisão sobre o caso atual. |

## 16. Critérios de Aceite (Gherkin)

```gherkin
Funcionalidade: Painel do analista com fila priorizada, cartão do caso e ações estruturadas

  Cenário: Fila é exibida priorizada por risco
    Dado que existem casos nas filas normal, reforçada e revisão manual
    Quando o analista abre o painel
    Então os casos devem ser exibidos respeitando o roteamento e a sugestão de prioridade já calculados

  Cenário: Cartão do caso reflete o conteúdo da feature 2.6 sem reprocessamento
    Dado um caso com alerta já montado pela feature de geração de alertas
    Quando o analista abre o cartão do caso
    Então o conteúdo exibido deve ser idêntico ao produzido pela feature de geração de alertas

  Cenário: Ação exige justificativa obrigatória
    Dado que o analista seleciona uma das 4 ações disponíveis
    Quando o analista tenta confirmar a ação sem preencher a justificativa
    Então a confirmação deve ser bloqueada até que a justificativa seja preenchida

  Cenário: Pedir documentos move o caso para estado "aguardando documentos"
    Dado que o analista escolhe a ação "pedir documentos" com justificativa preenchida
    Quando a ação é confirmada
    Então o caso deve passar para o estado "aguardando documentos"
    E deve ser exibido em uma seção separada da fila ativa

  Cenário: Caso retorna à fila ativa quando a documentação é recebida
    Dado um caso em estado "aguardando documentos"
    Quando o Sistema de Sinistros sinaliza o recebimento da documentação
    Então o caso deve retornar automaticamente à fila ativa

  Cenário: Encaminhar oferece apenas os destinos definidos
    Dado que o analista escolhe a ação "encaminhar"
    Quando o painel exibe as opções de destino
    Então apenas "Compliance" e "Investigação Especializada" devem estar disponíveis

  Cenário: Nenhuma ação decide o mérito do sinistro
    Dado qualquer uma das 4 ações disponíveis no painel
    Quando o analista a executa
    Então nenhuma decisão de cobertura ou indenização deve ser tomada pelo motor antifraude

  Cenário: Decisão do analista é registrada de forma auditável
    Dado que o analista confirma uma ação com justificativa
    Quando a decisão é registrada
    Então o sistema deve armazenar a ação, a justificativa, o analista e o timestamp de forma imutável

  Cenário: Atalho de histórico funciona mesmo com falha na consulta
    Dado que a base de histórico do cliente/aparelho está indisponível
    Quando o analista usa o atalho de histórico
    Então o painel deve exibir um erro claro
    E as demais funções do painel devem continuar disponíveis
```

## 17. KPIs

- Tempo médio de análise por caso (do momento em que entra na fila até a decisão registrada).
- Distribuição de decisões por tipo de ação (aprovar análise reforçada / pedir documentos / encaminhar / falso positivo).
- Taxa de uso do atalho de histórico completo (indicador de quanto a correlação de 3 itens já é suficiente ou não).
- % de casos revisados dentro do SLA por fila.
- Taxa de concordância entre a faixa calculada e a decisão final do analista (calibração).

## 18. Riscos

| Risco | Mitigação |
|---|---|
| Analista confunde "aprovar análise reforçada" com "aprovar o sinistro" | Copy e design da interface devem deixar essa distinção inequívoca; validação de linguagem com analistas reais antes do lançamento |
| Justificativa de texto livre se torna um campo preenchido de forma genérica ("ok", "revisado") sem valor real de auditoria | Métrica de qualidade de justificativa (ex.: tamanho mínimo) pode ser considerada no roadmap; não bloquear o fluxo por isso nesta fase |
| Atalho de histórico expõe volume grande de dados de outros sinistros sem necessidade | Manter os mesmos controles de acesso do sistema de sinistros; não duplicar dados no painel |

## 19. Dependências

- Contrato de comando entre o painel e o Sistema de Sinistros para a ação "pedir documentos" (formato, canal) e para o sinal de retorno de documentação recebida.
- Contrato de integração com Compliance e Investigação Especializada para receber casos encaminhados.
- Autenticação e controle de acesso de analistas já disponível na infraestrutura da ACME.

## 20. Itens Fora do Escopo (desta feature)

- Cálculo de score, faixa, roteamento e montagem do alerta (features 2.3 a 2.6).
- Decisão de mérito do sinistro (cobertura, indenização).
- Persistência do feedback para retreinamento do modelo (feature 2.8).
- Notificação direta ao cliente final (a comunicação, quando necessária, passa pelo Sistema de Sinistros).

## 21. Roadmap Futuro

1. Métricas de qualidade da justificativa registrada, com sugestões de preenchimento mais completo.
2. Visualização de tendências (ex.: gráfico de volume por faixa ao longo do tempo) diretamente no painel.
3. Ações em lote para casos de baixo risco com padrão muito semelhante (mantendo a decisão individual do analista, não uma aprovação em massa automática).
4. Definição de prazo/escalonamento para casos em "aguardando documentos" sem retorno do cliente por período prolongado.

## 22. Glossário

| Termo | Definição |
|---|---|
| **Cartão do caso** | Representação visual do alerta completo (feature 2.6) exibida ao analista no painel. |
| **Aprovar análise reforçada** | Ação que confirma que o caso segue em investigação antifraude mais profunda — não decide o mérito do sinistro. |
| **Encaminhar** | Ação que move o caso para outra fila ou time especializado, dentro do fluxo antifraude. |
| **Falso positivo** | Marcação do analista indicando que os indícios calculados não configuram suspeita real, usada como insumo para calibração futura. |
| **Aguardando documentos** | Estado do caso após a ação "pedir documentos", exibido em seção separada da fila até o retorno da documentação. |
| **Atalho de histórico** | Consulta rápida ao histórico completo de sinistros de um cliente/aparelho, além dos até 3 sinistros correlacionados já trazidos no alerta. |
