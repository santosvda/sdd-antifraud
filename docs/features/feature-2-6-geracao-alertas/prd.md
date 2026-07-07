# PRD — Motor Antifraude de Sinistros
## Feature 2.6: Geração de Alertas com Justificativa
**ACME Seguros — Seguro de Celular — Trilha B (sinistro por foto)**

---

## 1. Visão Geral

Esta feature monta o **alerta completo** que o analista efetivamente lê: reúne a evidência detalhada de cada sinal (feature 2.2), a faixa e a explicação resumida (feature 2.4) e a fila de destino (feature 2.5), e produz um pacote único — **lista de indícios com evidência, justificativa legível, correlação com sinistros semelhantes e sugestão de prioridade**. É a última etapa do motor antes do caso chegar ao painel do analista (feature futura, fora desta PRD).

Esta feature **não** recalcula nada — não gera novo score, não reclassifica faixa, não decide fila. Ela **compõe** o que as features anteriores já produziram em um formato pronto para leitura humana, e adiciona duas capacidades novas: **correlação entre sinistros semelhantes** e **sugestão de prioridade de análise**.

## 2. Problema

Sem esta feature, o analista teria que abrir múltiplas fontes (score, evidência de cada sinal, histórico de sinistros relacionados) para montar o quadro completo de um caso — o que aumenta o tempo de análise e o risco de o analista não perceber um padrão relevante (ex.: o mesmo aparelho aparecendo em outros sinistros). Também sem uma camada dedicada, a consistência da linguagem não acusatória fica sujeita a cada consumidor reimplementar a regra por conta própria.

## 3. Objetivos

- Montar, para cada caso roteado, um alerta com a lista completa dos 3 sinais e sua evidência específica (incluindo os marcados como "indisponível").
- Reaproveitar a justificativa resumida da feature 2.4 e compô-la com o detalhe de evidência por sinal, mantendo a mesma garantia de linguagem não acusatória.
- Correlacionar o caso com sinistros semelhantes já identificados pelos sinais (ex.: o sinistro que colidiu no hash de imagem, ou outros sinistros do mesmo cliente/aparelho no sinal de velocity).
- Sugerir uma prioridade de análise dentro da fila, com base em critérios explícitos e auditáveis — sempre como sugestão, nunca como decisão automática.
- Garantir que casos sem classificação (fail-open ou anomalia técnica) também recebam um alerta, claramente identificado como tal, em vez de ficarem sem nenhuma informação para o analista.

**Não-objetivos desta feature:** calcular score, classificar faixa, decidir roteamento de fila, construir a UI do painel do analista.

## 4. Personas

| Persona | Papel nesta feature |
|---|---|
| **Feature 2.2 (Coleta de Sinais)** | Fonte da evidência detalhada de cada sinal. |
| **Feature 2.4 (Classificação de Risco)** | Fonte da faixa e da explicação resumida. |
| **Feature 2.5 (Roteamento)** | Fonte da fila de destino e da sub-prioridade (quando em saturação). |
| **Analista de sinistro** | Consumidor final — lê o alerta completo para decidir o caso. |
| **Compliance** | Audita se o alerta mantém a linguagem não acusatória em toda a composição, não só na frase-resumo da feature 2.4. |

## 5. Jornada (Analista)

1. O caso chega roteado (feature 2.5), com evidência de sinais (feature 2.2) e faixa/explicação (feature 2.4), ou marcado como "sem classificação".
2. Esta feature monta o alerta: lista de indícios com evidência, justificativa legível, sinistros correlacionados (se houver), e uma sugestão de prioridade.
3. O analista abre o caso e vê tudo already montado: não precisa consultar fontes separadas para entender por que o caso está ali e quão urgente ele é frente aos demais da fila.
4. Se o caso está marcado como "sem classificação", o alerta deixa isso evidente logo no topo, sem tentar simular uma faixa ou prioridade que não existe.

## 6. Fluxo Completo (com caminho de "sem classificação")

```
[Evidência de sinais] (Feature 2.2) ──┐
[Faixa + explicação] (Feature 2.4) ───┼──▶ ┌─────────────────────────────┐
[Fila de destino] (Feature 2.5) ──────┘    │ Agregador de Alerta            │
                                             └───────────────┬───────────────┘
                                                             ▼
                                             ┌─────────────────────────────┐
                                             │ Caso tem faixa classificada?  │
                                             └───────┬───────────────┬─────┘
                                                 sim │               │ não
                                                     ▼               ▼
                                   ┌─────────────────────┐  ┌───────────────────────────┐
                                   │ Monta lista de          │  │ Monta alerta "sem            │
                                   │ indícios + evidência     │  │ classificação": lista de     │
                                   │ (inclui sinais            │  │ indícios disponíveis (se     │
                                   │ "indisponível")           │  │ houver), aviso explícito de  │
                                   └──────────┬───────────────┘  │ ausência de score/faixa       │
                                              ▼                  └───────────┬───────────────────┘
                                   ┌─────────────────────┐                  │
                                   │ Busca Sinistros          │                  │
                                   │ Correlacionados           │                  │
                                   │ (via evidência de reuso   │                  │
                                   │ de imagem e velocity)     │                  │
                                   └──────────┬───────────────┘                  │
                                              ▼                                  │
                                   ┌─────────────────────┐                  │
                                   │ Calcula Sugestão de       │                  │
                                   │ Prioridade (score+valor)  │                  │
                                   └──────────┬───────────────┘                  │
                                              ▼                                  ▼
                                   ┌─────────────────────────────────────────────┐
                                   │ Registro de Auditoria do Alerta                │
                                   └───────────────────────┬─────────────────────────┘
                                                            ▼
                                              Alerta pronto para o
                                              Painel do Analista (feature futura)
```

**Ponto crítico do guardrail:** mesmo no ramo "sem classificação", um alerta **é gerado** — nunca fica um caso silenciosamente sem nada visível ao analista só porque a IA falhou upstream.

## 7. Regras de Negócio

1. O alerta só é montado depois que a feature 2.5 já roteou o caso (ou seja, esta feature é a última etapa, não paralela ao roteamento).
2. A lista de indícios exibe os 3 sinais sempre, no mesmo formato, independentemente do valor: ativo (com evidência), inativo, ou indisponível (com o motivo).
3. A justificativa legível reaproveita a explicação gerada pela feature 2.4 como base, adicionando o detalhe de evidência por sinal — sem jamais adicionar linguagem que contradiga a regra de não-acusação já estabelecida (nunca "fraude confirmada", sempre "indícios").
4. A correlação com sinistros semelhantes usa exclusivamente evidência já produzida pelos sinais: o sinistro que colidiu no hash de imagem (sinal de reuso) e os demais sinistros do mesmo cliente/aparelho contabilizados no sinal de velocity. O alerta exibe o **ID do sinistro correlacionado diretamente** (sem outros dados do caso relacionado), limitado a **até 3 sinistros**, ordenados do mais recente para o mais antigo, com indicação textual de "+N outros" quando houver mais de 3.
5. A sugestão de prioridade dentro da fila usa o mesmo critério já estabelecido para sub-priorização na feature 2.5 (score decrescente, depois valor do sinistro decrescente) — aplicado aqui de forma consistente mesmo fora de situação de saturação, para que a ordem sugerida seja sempre a mesma lógica, saturada ou não.
6. Casos "sem classificação" recebem um alerta próprio, com aviso explícito de que não há score/faixa disponível, sem sugestão de prioridade calculada (fica marcado para revisão assim que possível, sem posição relativa simulada).
7. Nenhuma informação incluída no alerta pode ser um atributo sensível proibido, mesmo indiretamente — a composição herda essa garantia das features anteriores, mas esta feature não deve introduzir nenhum dado novo que viole essa regra.
8. Todo alerta gerado é auditável e reproduzível: mesma entrada (sinais, faixa, fila) produz sempre o mesmo alerta.

## 8. Arquitetura de Alto Nível

```
┌───────────────┐ ┌───────────────┐ ┌───────────────┐
│ Feature 2.2      │ │ Feature 2.4      │ │ Feature 2.5      │
│ (evidência de     │ │ (faixa +          │ │ (fila de          │
│ sinais)           │ │ explicação)       │ │ destino)          │
└────────┬──────────┘ └────────┬──────────┘ └────────┬──────────┘
         └─────────────────────┼─────────────────────┘
                               ▼
          ┌──────────────────────────────────────┐
          │ FEATURE 2.6 — Geração de Alertas          │
          │                                          │
          │  ┌───────────────────────────┐         │
          │  │ Montador de Lista de           │        │
          │  │ Indícios + Evidência            │        │
          │  └─────────────┬─────────────────┘        │
          │                ▼                          │
          │  ┌───────────────────────────┐         │
          │  │ Buscador de Correlação          │◀────┼── Base de Sinistros
          │  │ (sinistros semelhantes)         │        │  (consulta por hash/
          │  └─────────────┬─────────────────┘        │   cliente/aparelho)
          │                ▼                          │
          │  ┌───────────────────────────┐         │
          │  │ Calculador de Sugestão de      │        │
          │  │ Prioridade                     │        │
          │  └─────────────┬─────────────────┘        │
          │                ▼                          │
          │  ┌───────────────────────────┐         │
          │  │ Publicador de Auditoria         │────────┼──▶ Log imutável
          │  └─────────────┬─────────────────┘        │
          └────────────────┼─────────────────────────────┘
                           ▼
              Painel do Analista (feature futura)
```

## 9. Requisitos Funcionais

| ID | Requisito |
|---|---|
| RF01 | O sistema deve montar a lista dos 3 sinais com seu estado (ativo/inativo/indisponível) e evidência específica. |
| RF02 | O sistema deve compor a justificativa legível reaproveitando a explicação da feature 2.4, sem introduzir linguagem acusatória adicional. |
| RF03 | O sistema deve identificar sinistros correlacionados a partir da evidência de reuso de imagem e de velocity, exibindo o ID de até 3 sinistros (mais recentes primeiro) com indicação de "+N outros" quando houver mais. |
| RF04 | O sistema deve calcular uma sugestão de prioridade usando score decrescente e valor do sinistro decrescente como critérios, consistente com a lógica de sub-priorização da feature 2.5. |
| RF05 | O sistema deve gerar um alerta próprio para casos "sem classificação", com aviso explícito da ausência de score/faixa, sem prioridade calculada. |
| RF06 | O sistema não deve incluir nenhum atributo sensível proibido no alerta, mesmo que presente incidentalmente em algum dado de origem. |
| RF07 | O sistema deve registrar de forma auditável o conteúdo completo do alerta gerado para cada caso. |
| RF08 | A geração do alerta deve ser determinística: mesma entrada produz sempre o mesmo alerta. |

## 10. Requisitos Não Funcionais

- **Latência:** montagem do alerta é composição de dados já calculados; não deve adicionar latência perceptível ao orçamento de SLA (≤5 min p95 total).
- **Determinismo:** requisito formal (RF08).
- **Auditabilidade:** todo alerta gerado é registrado de forma imutável.
- **Consistência:** o mesmo caso deve gerar o mesmo alerta independentemente de quando ou quantas vezes for consultado (idempotência de leitura).

## 11. Integrações

- **Feature 2.2 (Coleta de Sinais)** — evidência detalhada por sinal.
- **Feature 2.4 (Classificação de Risco)** — faixa e explicação resumida (ou marca de "sem classificação").
- **Feature 2.5 (Roteamento)** — fila de destino e eventual sub-prioridade de saturação.
- **Base de Sinistros** — consultada para resolver os sinistros correlacionados (via ID já presente na evidência de reuso de imagem e velocity).
- **Log/Auditoria** — armazenamento imutável do alerta completo gerado.
- **Painel do Analista (feature futura)** — consumidor final do alerta.

## 12. Segurança e LGPD

- Esta feature não deve introduzir dados pessoais além dos já processados pelas features anteriores (IDs técnicos, evidência de sinais, score, faixa).
- A correlação com sinistros semelhantes expõe o **ID técnico** de até 3 sinistros relacionados diretamente no alerta, sem nenhum outro dado do caso relacionado (nome, valor, etc.) — o detalhe completo do sinistro correlacionado, se necessário, é acessado pelo analista através dos controles de acesso já existentes no sistema de sinistros.
- Acesso ao alerta segue a segregação já definida entre analista e compliance.

## 13. Auditoria

Para cada alerta gerado, registrar de forma imutável:
- ID do sinistro, lista completa de sinais com estado e evidência.
- Justificativa legível composta.
- Lista de sinistros correlacionados (se houver) e o sinal que motivou a correlação.
- Sugestão de prioridade calculada (ou marca de "sem prioridade calculada" para casos sem classificação).
- Timestamp de geração do alerta.

## 14. Casos de Uso

1. **Alerta completo padrão:** caso com faixa "alto", 2 sinais ativos e 1 inativo → alerta com lista completa, justificativa, correlação (se houver) e prioridade sugerida.
2. **Alerta com sinal indisponível:** um dos 3 sinais está marcado como "indisponível" (herdado da feature 2.2) → alerta exibe esse sinal como indisponível, com o motivo, sem tratá-lo como ativo nem inativo.
3. **Correlação via reuso de imagem:** sinal de reuso ativo → alerta lista o ID do sinistro específico que colidiu no hash, com a distância calculada.
4. **Correlação via velocity com muitos sinistros:** sinal de velocity ativo com 5 sinistros no histórico → alerta lista os IDs dos 3 mais recentes, com indicação "+2 outros".
5. **Alerta "sem classificação":** caso chegou em fail-open ou anomalia técnica → alerta indica claramente a ausência de score/faixa, sem sugestão de prioridade, priorizando transparência sobre a limitação da análise automática.

## 15. Casos de Exceção

| Exceção | Tratamento |
|---|---|
| Caso "sem classificação" (fail-open ou anomalia técnica) | Gera alerta próprio, com aviso explícito, sem prioridade calculada. |
| Sinal indisponível (herdado da feature 2.2) | Exibido no alerta como "indisponível" com o motivo, nunca omitido nem tratado como inativo. |
| Base de Sinistros indisponível ao buscar correlação | Alerta é gerado normalmente, sem a seção de correlação, marcada como "não verificado" (não como "nenhum sinistro correlacionado"). |
| Valor do sinistro indisponível para a sugestão de prioridade | Sugestão de prioridade usa apenas o score como critério. |

## 16. Critérios de Aceite (Gherkin)

```gherkin
Funcionalidade: Geração de alerta completo com justificativa, correlação e prioridade

  Cenário: Alerta lista todos os sinais com seu estado e evidência
    Dado um caso com os 3 sinais calculados, sendo um deles indisponível
    Quando a feature de geração de alertas monta o caso
    Então o alerta deve listar os 3 sinais com seus estados (ativo, inativo ou indisponível)
    E cada sinal ativo deve conter sua evidência específica

  Cenário: Correlação via reuso de imagem aponta o sinistro colidido
    Dado um caso com o sinal de reuso de imagem ativo
    Quando o alerta é montado
    Então a lista de sinistros correlacionados deve incluir o ID do sinistro que colidiu no hash

  Cenário: Correlação limita a exibição a 3 sinistros com indicação de excedente
    Dado um caso com o sinal de velocity ativo apontando 5 sinistros correlacionados
    Quando o alerta é montado
    Então o alerta deve exibir os IDs dos 3 sinistros mais recentes
    E deve indicar textualmente que há 2 outros sinistros além dos exibidos

  Cenário: Sugestão de prioridade segue o mesmo critério da sub-priorização
    Dado dois casos na mesma fila, um com score mais alto que o outro
    Quando os alertas são gerados
    Então o caso de score mais alto deve receber sugestão de prioridade maior

  Cenário: Caso sem classificação gera alerta com aviso explícito
    Dado um caso marcado como "sem classificação" pela feature de classificação
    Quando o alerta é montado
    Então o alerta deve indicar explicitamente a ausência de score e faixa
    E nenhuma sugestão de prioridade deve ser calculada para esse caso

  Cenário: Base de Sinistros indisponível não impede a geração do alerta
    Dado que a Base de Sinistros está indisponível ao buscar correlação
    Quando o alerta é montado
    Então o alerta deve ser gerado normalmente
    E a seção de correlação deve ser marcada como "não verificado"

  Cenário: Geração de alerta é determinística
    Dado um conjunto fixo de sinais, faixa e fila
    Quando o alerta é gerado duas vezes para essa mesma entrada
    Então o conteúdo do alerta deve ser idêntico nas duas execuções

  Cenário: Auditoria registra o alerta completo
    Dado um caso processado pela feature de geração de alertas
    Quando o alerta é concluído
    Então o sistema deve registrar a lista de sinais, a justificativa, a correlação e a prioridade sugerida
    E esse registro deve ser imutável
```

## 17. KPIs

- Taxa de alertas com pelo menos um sinal indisponível.
- Taxa de alertas com correlação encontrada (reuso de imagem ou velocity).
- Taxa de alertas "sem classificação".
- Taxa de concordância entre a prioridade sugerida e a ordem efetiva de análise do analista (proxy de utilidade da sugestão).

## 18. Riscos

| Risco | Mitigação |
|---|---|
| Correlação incompleta (só linka sinistros que já geraram sinal, não todo padrão possível) gera falsa sensação de completude | Deixar claro na UI (fora desta PRD) que a correlação é limitada aos sinais calculados, não uma busca exaustiva |
| Sugestão de prioridade sempre igual à sub-priorização de saturação pode não refletir bem casos fora de saturação | Métrica de concordância analista×sugestão monitorada; ajustar critério no roadmap se necessário |
| Justificativa composta (2.4 + evidência) ficar longa demais e prejudicar a leitura rápida do analista | Validar formato com analistas reais antes de finalizar o template de composição |

## 19. Dependências

- Acesso de leitura à Base de Sinistros para resolver os identificadores correlacionados.
- Ordenação por data disponível na Base de Sinistros, para aplicar o critério "mais recentes primeiro" no limite de 3 sinistros exibidos.

## 20. Itens Fora do Escopo (desta feature)

- Cálculo de score, classificação de faixa e decisão de roteamento (features 2.3, 2.4, 2.5).
- UI do painel do analista (feature futura — esta feature apenas produz o conteúdo do alerta).
- Ações do analista sobre o caso (aprovar, pedir documentos, marcar falso positivo) — feature futura de painel.
- Feedback loop de recalibração do modelo a partir da decisão do analista — feature futura.

## 21. Roadmap Futuro

1. Correlação mais ampla, além de reuso de imagem e velocity (ex.: padrões de rede de fraude entre múltiplos clientes).
2. Sugestão de prioridade mais sofisticada, incorporando tempo de espera acumulado como fator adicional.
3. Personalização do nível de detalhe do alerta conforme perfil do analista (júnior vs. sênior).

## 22. Glossário

| Termo | Definição |
|---|---|
| **Alerta** | Pacote completo de informação sobre um caso, composto por lista de indícios, justificativa, correlação e sugestão de prioridade. |
| **Sinistro correlacionado** | Outro sinistro identificado como relacionado ao caso atual, via evidência de reuso de imagem ou velocity. |
| **Sugestão de prioridade** | Ordenação recomendada dentro da fila, baseada em score e valor do sinistro — sempre sugestão, nunca decisão automática. |
| **Sem classificação** | Estado de um caso que chegou sem faixa de risco disponível (fail-open ou anomalia técnica), tratado com alerta próprio e transparente sobre a limitação. |
