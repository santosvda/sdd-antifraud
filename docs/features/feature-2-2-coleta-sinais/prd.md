# PRD — Motor Antifraude de Sinistros
## Feature 2.2: Coleta de Sinais
**ACME Seguros — Seguro de Celular — Trilha B (sinistro por foto)**

---

## 1. Visão Geral

Esta feature é a **camada de coleta**: recebe o caso já ingerido e validado pela feature 2.1, e calcula, de forma independente e paralela, os **3 sinais fixos** desta fatia do motor antifraude — **reuso de imagem** (hash perceptual comparado ao histórico), **inconsistência IMEI×série×apólice** e **velocity** (frequência de sinistros do mesmo cliente/aparelho). Cada sinal é calculado isoladamente: a falha ou ausência de dado para um sinal nunca impede o cálculo dos demais, nem trava o caso — ele segue para a feature 2.3 (Score & Regras) com o que foi possível calcular, exatamente como a lógica de renormalização de pesos já prevê.

Esta feature **não** calcula o score, **não** decide faixa de risco e **não** implementa (nesta fatia) os demais sinais do catálogo do produto (detecção de edição de imagem, EXIF, geolocalização, aparelho já indenizado) — esses ficam para o roadmap.

## 2. Problema

Sem uma camada de coleta centralizada e com contrato bem definido, cada sinal poderia ser calculado de forma ad-hoc, com formatos inconsistentes, sem tratamento padronizado de indisponibilidade de fonte de dados, e sem evidência auditável — dificultando tanto a fase de score (que depende de sinais bem-formados para renormalizar) quanto a auditoria e o monitoramento de viés exigidos pelo produto.

## 3. Objetivos

- Calcular os 3 sinais fixos desta fatia de forma independente e paralela.
- Comparar o hash perceptual das fotos do sinistro atual com o histórico de sinistros anteriores, para detectar reuso de imagem.
- Verificar consistência entre IMEI, número de série e apólice.
- Calcular o sinal de velocity conforme a regra já fechada (≥2 sinistros do mesmo cliente OU aparelho em 90 dias).
- Marcar explicitamente um sinal como **"indisponível"** (nunca como falso) quando não for possível calculá-lo, seja por dado ausente no payload ou por indisponibilidade de uma fonte externa.
- Gerar evidência auditável para cada sinal calculado (o que exatamente motivou o valor do sinal).

**Não-objetivos desta feature:** calcular score ou faixa de risco, decidir roteamento, implementar os demais sinais do catálogo do produto (edição de imagem, EXIF, geolocalização, aparelho já indenizado).

## 4. Personas

| Persona | Papel nesta feature |
|---|---|
| **Feature 2.1 (Ingestão)** | Produtora do caso (com eventual marca de "payload parcial") que alimenta esta feature. |
| **Feature 2.3 (Score & Regras)** | Consumidora direta dos 3 sinais calculados aqui. |
| **Equipe antifraude** | Consome métricas de disponibilidade e evidência dos sinais para diagnosticar qualidade de dados e calibrar o motor de score. |
| **Compliance** | Audita a evidência de cada sinal, para garantir que nenhum atributo proibido é usado como proxy em qualquer sinal. |

## 5. Jornada (Equipe Antifraude)

1. O caso chega da fila da feature 2.1, com ou sem marca de "payload parcial".
2. Esta feature tenta calcular, em paralelo, os 3 sinais: reuso de imagem, IMEI×série, velocity.
3. Cada sinal segue seu próprio caminho: se o dado de entrada necessário está no payload e a fonte externa está disponível, o sinal é calculado (verdadeiro/falso); caso contrário, o sinal é marcado como "indisponível".
4. O conjunto de sinais (calculados e/ou indisponíveis), com suas evidências, é publicado para a feature 2.3.
5. Equipe antifraude acompanha, via métricas expostas, a taxa de indisponibilidade por sinal — um sinal cronicamente indisponível costuma indicar problema de integração com a fonte de dados correspondente, não necessariamente ausência real de risco.

## 6. Fluxo Completo (com caminho de indisponibilidade por sinal)

```
[Caso vindo da Feature 2.1] (pode estar marcado como "payload parcial")
                │
                ├──────────────────┬──────────────────────┐
                ▼                  ▼                       ▼
    ┌─────────────────────┐ ┌─────────────────────┐ ┌─────────────────────┐
    │ Sinal: Reuso de Imagem │ │ Sinal: IMEI×Série     │ │ Sinal: Velocity       │
    └──────────┬─────────────┘ └──────────┬─────────────┘ └──────────┬─────────────┘
               │                          │                          │
      há foto(s) no payload?     há IMEI/série no payload?   há ID de cliente/
               │                          │                  aparelho no payload?
       sim │      │ não            sim │      │ não             sim │      │ não
           ▼      ▼                    ▼      ▼                     ▼      ▼
   ┌───────────┐ ┌──────────────┐ ┌───────────┐ ┌──────────────┐ ┌───────────┐ ┌──────────────┐
   │Repositório  │ │Sinal =        │ │Base de      │ │Sinal =        │ │Histórico de │ │Sinal =        │
   │de Imagens   │ │"indisponível" │ │Apólices     │ │"indisponível" │ │Sinistros    │ │"indisponível" │
   │DISPONÍVEL?  │ │(sem foto)     │ │DISPONÍVEL?  │ │(sem IMEI)     │ │DISPONÍVEL?  │ │(sem ID)       │
   └──┬───────┬──┘ └──────────────┘ └──┬───────┬──┘ └──────────────┘ └──┬───────┬──┘ └──────────────┘
  sim │       │ não                sim │       │ não                sim │       │ não
      ▼       ▼                        ▼       ▼                        ▼       ▼
 ┌────────┐ ┌──────────────┐     ┌────────┐ ┌──────────────┐     ┌────────┐ ┌──────────────┐
 │Compara   │ │Sinal =        │     │Consulta  │ │Sinal =        │     │Consulta  │ │Sinal =        │
 │hash com   │ │"indisponível" │     │IMEI×série│ │"indisponível" │     │≥2 em 90d │ │"indisponível" │
 │histórico  │ │(fonte fora)   │     │×apólice  │ │(fonte fora)   │     │mesmo     │ │(fonte fora)   │
 │           │ │               │     │          │ │               │     │cliente/  │ │               │
 │→ true/    │ │               │     │→ true/   │ │               │     │aparelho  │ │               │
 │  false    │ │               │     │  false   │ │               │     │→ true/   │ │               │
 └────┬─────┘ └──────────────┘     └────┬─────┘ └──────────────┘     │false     │ └──────────────┘
      │                                  │                            └────┬─────┘
      └──────────────────┬───────────────┴─────────────────────────────────┘
                          ▼
              ┌─────────────────────────────┐
              │ Agregador de Sinais            │
              │ (evidência de cada sinal,       │
              │ inclui "indisponível" quando     │
              │ aplicável)                       │
              └───────────────┬───────────────────┘
                              ▼
              ┌─────────────────────────────┐
              │ Registro de Auditoria          │
              └───────────────┬───────────────────┘
                              ▼
                Saída para Feature 2.3
                (Score & Regras)
```

**Ponto crítico do guardrail:** a indisponibilidade de qualquer fonte (repositório de imagens, base de apólices, histórico de sinistros) afeta **apenas o sinal correspondente** — nunca impede o cálculo dos outros dois, nem impede o caso de seguir para a feature 2.3.

## 7. Regras de Negócio

1. Os 3 sinais são calculados de forma **independente e paralela** — falha ou indisponibilidade em um não afeta os demais.
2. Um sinal é marcado como **"indisponível"** (não como "falso") quando: (a) o dado de entrada necessário está ausente no payload (ex.: sem foto, sem IMEI, sem ID de cliente/aparelho), ou (b) a fonte externa necessária para o cálculo está indisponível no momento.
3. **Reuso de imagem:** compara o hash perceptual (**pHash, 64 bits**) das fotos do sinistro atual com o histórico de hashes de sinistros dos **últimos 6 meses**. Reuso é confirmado quando a **distância de Hamming for ≤ 10**.
4. **IMEI×série×apólice:** consulta a base de apólices para verificar se o IMEI/número de série informado no sinistro corresponde ao registrado na apólice. O sinal é ativado tanto quando o IMEI **diverge** do cadastrado quanto quando está **não cadastrado** — nos dois casos o sinal é o mesmo (verdadeiro), mas a evidência registrada distingue os dois motivos.
5. **Velocity:** ativo quando há ≥2 sinistros do mesmo cliente OU mesmo aparelho (IMEI) em janela de 90 dias (regra já fechada na feature 2.3, aplicada aqui na coleta).
6. Todo sinal calculado (verdadeiro, falso ou indisponível) deve carregar evidência: para reuso de imagem, qual sinistro colidiu e a distância de similaridade; para IMEI×série, os valores comparados; para velocity, a contagem e a janela usada.
7. Um sinal marcado como "indisponível" nunca é convertido em "falso" por conveniência — essa distinção é o que permite à feature 2.3 aplicar a renormalização corretamente.
8. Nenhum dos 3 sinais utiliza atributos sensíveis proibidos, direta ou indiretamente, como parte do seu cálculo.

## 8. Arquitetura de Alto Nível

```
┌─────────────────────────┐
│ Feature 2.1 (Ingestão)    │
└────────────┬───────────────┘
             │ caso (com marca de payload parcial, se houver)
             ▼
┌──────────────────────────────────────────────────┐
│ FEATURE 2.2 — Coleta de Sinais                       │
│                                                      │
│  ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│  │ Calculador:        │ │ Calculador:        │ │ Calculador:        │
│  │ Reuso de Imagem     │ │ IMEI×Série          │ │ Velocity            │
│  └────────┬────────────┘ └────────┬────────────┘ └────────┬────────────┘
│           │ timeout/circuit        │ timeout/circuit        │ timeout/circuit
│           ▼ breaker                ▼ breaker                ▼ breaker
│  ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│  │ Repositório de     │ │ Base de Apólices    │ │ Histórico de       │
│  │ Imagens             │ │                     │ │ Sinistros           │
│  └─────────────────┘ └─────────────────┘ └─────────────────┘
│           │                        │                        │
│           └────────────┬───────────┴────────────┬────────────┘
│                        ▼                        ▼
│              ┌───────────────────────────────────┐
│              │ Agregador de Sinais + Auditoria       │
│              └───────────────────┬─────────────────────┘
└──────────────────────────────────┼──────────────────────────┘
                                   ▼
                     Feature 2.3 (Score & Regras)
```

## 9. Requisitos Funcionais

| ID | Requisito |
|---|---|
| RF01 | O sistema deve calcular o sinal de reuso de imagem usando pHash (64 bits) e comparar com o histórico dos últimos 6 meses, confirmando reuso quando a distância de Hamming for ≤ 10. |
| RF02 | O sistema deve calcular o sinal de inconsistência IMEI×série consultando a base de apólices, ativando o sinal tanto para IMEI divergente quanto para IMEI não cadastrado, com evidência que distinga os dois motivos. |
| RF03 | O sistema deve calcular o sinal de velocity com base em ≥2 sinistros do mesmo cliente ou aparelho em 90 dias. |
| RF04 | O sistema deve calcular os 3 sinais de forma independente e paralela, sem que a falha de um impacte os demais. |
| RF05 | O sistema deve marcar um sinal como "indisponível" quando o dado de entrada necessário estiver ausente no payload. |
| RF06 | O sistema deve marcar um sinal como "indisponível" quando a fonte externa necessária estiver inacessível, aplicando timeout/circuit breaker. |
| RF07 | O sistema deve registrar evidência específica para cada sinal calculado (colisão de hash, valores IMEI×série comparados, contagem de velocity). |
| RF08 | O sistema deve publicar o conjunto de sinais (com evidências e eventuais indisponibilidades) para a feature 2.3, mantendo a marca de "payload parcial" herdada da feature 2.1 quando aplicável. |

## 10. Requisitos Não Funcionais

- **Assíncrono e paralelo:** os 3 sinais são calculados concorrentemente, não em série.
- **Latência:** cálculo dos 3 sinais concluído dentro do orçamento do SLA total (≤5 min p95 ponta a ponta).
- **Resiliência:** timeout/circuit breaker independente por fonte de dados (repositório de imagens, base de apólices, histórico de sinistros).
- **Observabilidade:** métricas de disponibilidade por sinal, taxa de sinal ativado/indisponível, latência por fonte.
- **Auditabilidade:** toda evidência de sinal é registrada de forma imutável.

## 11. Integrações

- **Feature 2.1 (Ingestão)** — fonte do caso a ser processado.
- **Repositório de Imagens** — fornece hashes perceptuais das fotos do sinistro atual e do histórico, para o sinal de reuso de imagem.
- **Base de Apólices** — fornece IMEI/série registrado, para o sinal de IMEI×série.
- **Histórico de Sinistros** — fornece a contagem de sinistros por cliente/aparelho, para o sinal de velocity.
- **Feature 2.3 (Score & Regras)** — consumidora da saída desta feature.
- **Log/Auditoria** — armazenamento imutável da evidência de cada sinal.

## 12. Segurança e LGPD

- Minimização: apenas o hash perceptual é comparado, não a imagem bruta, sempre que possível.
- Mascaramento de dados sensíveis nos logs de evidência (ex.: IMEI/série tratados como identificadores técnicos, não expostos in-the-clear além do necessário).
- Base legal: legítimo interesse / prevenção à fraude, documentada — mesma base das demais features do motor.
- Acesso restrito às fontes de dados (repositório de imagens, base de apólices, histórico de sinistros) e aos registros de evidência.

## 13. Auditoria

Para cada caso processado, registrar de forma imutável, por sinal:
- Valor do sinal (verdadeiro / falso / indisponível).
- Evidência específica (ex.: ID do sinistro colidido e distância de similaridade, para reuso de imagem).
- Motivo da indisponibilidade, quando aplicável (dado ausente no payload vs. fonte externa inacessível).
- Timestamp do cálculo e identificador do sinistro.

## 14. Casos de Uso

1. **Todos os sinais calculados normalmente:** payload completo, todas as fontes disponíveis → 3 sinais com valor verdadeiro/falso e evidência.
2. **Reuso de imagem detectado:** hash pHash de uma foto do sinistro atual tem distância de Hamming ≤ 10 em relação ao hash de um sinistro dos últimos 6 meses → sinal verdadeiro, evidência aponta o sinistro colidido e a distância calculada.
3. **Sinal indisponível por dado ausente:** payload chegou marcado como "parcial" pela feature 2.1, sem IMEI → sinal de IMEI×série marcado como "indisponível" (motivo: dado ausente).
3a. **IMEI não cadastrado na apólice:** IMEI informado no sinistro não existe em nenhum registro da apólice → sinal de IMEI×série ativado (verdadeiro), evidência registra "não cadastrado" (distinto de "diverge").
4. **Sinal indisponível por fonte externa fora do ar:** payload completo, mas repositório de imagens está indisponível → sinal de reuso de imagem marcado como "indisponível" (motivo: fonte externa), enquanto os outros 2 sinais são calculados normalmente.
5. **Velocity ativado:** histórico mostra 3 sinistros do mesmo aparelho nos últimos 60 dias → sinal verdadeiro, evidência traz a contagem e a janela.

## 15. Casos de Exceção

| Exceção | Tratamento |
|---|---|
| Repositório de Imagens indisponível | Sinal de reuso de imagem marcado como "indisponível" (fonte externa); demais sinais seguem normalmente. |
| Base de Apólices indisponível | Sinal de IMEI×série marcado como "indisponível" (fonte externa); demais sinais seguem normalmente. |
| Histórico de Sinistros indisponível | Sinal de velocity marcado como "indisponível" (fonte externa); demais sinais seguem normalmente. |
| Payload sem foto (herdado de "payload parcial") | Sinal de reuso de imagem marcado como "indisponível" (dado ausente), sem tentar chamar o repositório de imagens. |
| Payload sem IMEI/série (herdado de "payload parcial") | Sinal de IMEI×série marcado como "indisponível" (dado ausente). |
| Payload sem ID de cliente/aparelho (herdado de "payload parcial") | Sinal de velocity marcado como "indisponível" (dado ausente). |
| Todos os 3 sinais indisponíveis | Caso ainda segue para a feature 2.3, que trata a ausência total de sinais como equivalente à indisponibilidade geral (sinaliza "não avaliado"). |

## 16. Critérios de Aceite (Gherkin)

```gherkin
Funcionalidade: Coleta independente dos 3 sinais de risco

  Cenário: Sinal de reuso de imagem é calculado com evidência
    Dado um sinistro com uma foto cujo hash pHash tem distância de Hamming menor ou igual a 10 em relação ao hash de um sinistro dos últimos 6 meses
    Quando a feature de coleta de sinais processa o caso
    Então o sinal de reuso de imagem deve ser "verdadeiro"
    E a evidência deve indicar o sinistro anterior colidido e a distância calculada

  Cenário: IMEI não cadastrado ativa o mesmo sinal que IMEI divergente
    Dado um sinistro cujo IMEI informado não existe em nenhum registro da apólice
    Quando a feature de coleta de sinais processa o caso
    Então o sinal de IMEI×série deve ser "verdadeiro"
    E a evidência deve indicar "não cadastrado", distinguindo esse motivo de uma divergência

  Cenário: Falha em uma fonte não impede o cálculo dos demais sinais
    Dado que o Repositório de Imagens está indisponível
    E a Base de Apólices e o Histórico de Sinistros estão disponíveis
    Quando a feature de coleta de sinais processa o caso
    Então o sinal de reuso de imagem deve ser marcado como "indisponível"
    E os sinais de IMEI×série e velocity devem ser calculados normalmente

  Cenário: Dado ausente no payload gera sinal indisponível, não falso
    Dado um caso marcado como "payload parcial" sem o campo de IMEI
    Quando a feature de coleta de sinais processa o caso
    Então o sinal de IMEI×série deve ser marcado como "indisponível"
    E esse sinal não deve ser tratado como "falso" pela feature de score

  Cenário: Velocity é calculado com a janela e contagem corretas
    Dado um histórico com 2 sinistros do mesmo aparelho nos últimos 90 dias
    Quando a feature de coleta de sinais processa o caso
    Então o sinal de velocity deve ser "verdadeiro"
    E a evidência deve indicar a contagem e a janela de 90 dias usada

  Cenário: Auditoria registra evidência de cada sinal
    Dado um caso processado pela feature de coleta de sinais
    Quando o processamento é concluído
    Então o sistema deve registrar o valor, a evidência e o motivo de eventual indisponibilidade de cada um dos 3 sinais
    E esse registro deve ser imutável
```

## 17. KPIs

- Taxa de disponibilidade por sinal (reuso de imagem, IMEI×série, velocity).
- Taxa de ativação de cada sinal (quantos casos disparam cada um).
- Latência de cálculo por fonte de dados.
- Taxa de casos com todos os 3 sinais indisponíveis (indicador de problema sistêmico de integração).

## 18. Riscos

| Risco | Mitigação |
|---|---|
| Limiar de similaridade (Hamming ≤10) pode ainda gerar falso positivo/negativo em casos-limite | Revisão junto com a calibração do score na equipe antifraude; ajustável via configuração se necessário no futuro |
| Consulta ao histórico de sinistros fica lenta em bases grandes, comprometendo o SLA | Índice dedicado por cliente/aparelho; timeout curto com fallback para "indisponível" |
| Indisponibilidade recorrente de uma fonte gera viés silencioso no score (sinal sempre "indisponível", nunca contribuindo) | Métrica de taxa de indisponibilidade por sinal monitorada; alerta se sair da banda esperada |

## 19. Dependências

- Índice/estrutura de armazenamento de hashes pHash com janela móvel de 6 meses (expurgo de hashes mais antigos).
- Definição da fonte de histórico de sinistros usada para o cálculo de velocity (pode ser o próprio Sistema de Sinistros ou um data store dedicado).
- Disponibilidade da Base de Apólices com IMEI/série associados a cada apólice.

## 20. Itens Fora do Escopo (desta feature)

- Cálculo do score e classificação de faixa de risco (feature 2.3).
- Roteamento por fila (feature 2.5).
- Detecção de edição de imagem, metadados EXIF, geolocalização, aparelho já indenizado (roadmap).
- Correlação de sinistros semelhantes além da comparação de hash (cross-case linking mais amplo).

## 21. Roadmap Futuro

1. Detecção de edição de imagem (compressão dupla, clonagem, adulteração de EXIF).
2. Sinal de geolocalização incompatível com o histórico do cliente (uso comportamental, nunca como "zona de risco").
3. Consulta a base de aparelhos já indenizados.
4. Extração e análise mais rica de metadados EXIF.
5. Correlação de sinistros semelhantes além de hash de imagem (cross-case linking).

## 22. Glossário

| Termo | Definição |
|---|---|
| **Hash perceptual (pHash)** | Assinatura digital de 64 bits de uma imagem que permite comparar similaridade visual entre fotos, mesmo com pequenas alterações (recompressão, redimensionamento). |
| **Distância de Hamming** | Métrica de diferença entre dois hashes; nesta feature, reuso de imagem é confirmado quando a distância for ≤ 10. |
| **Sinal indisponível** | Estado de um sinal que não pôde ser calculado (por dado ausente ou fonte externa fora do ar) — distinto de "falso", que indica ausência confirmada do padrão de risco. |
| **Evidência** | Informação específica que justifica o valor atribuído a um sinal (ex.: qual sinistro colidiu no hash). |
| **Velocity** | Sinal booleano ativado quando há ≥2 sinistros do mesmo cliente ou mesmo aparelho (IMEI) em janela de 90 dias. |
| **Circuit breaker** | Mecanismo que interrompe temporariamente chamadas a uma fonte de dados com falha recorrente, evitando esperas desnecessárias. |
