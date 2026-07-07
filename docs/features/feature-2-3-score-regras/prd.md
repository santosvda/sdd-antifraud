# PRD — Motor Antifraude de Sinistros
## Feature 2.3: Score & Regras
**ACME Seguros — Seguro de Celular — Trilha B (sinistro por foto)**

---

## 1. Visão Geral

Esta feature entrega o **motor de cálculo de score de risco**: dado um conjunto de 3 sinais coletados sobre um sinistro (reuso de imagem, inconsistência IMEI×série, velocity), o sistema combina esses sinais em um score numérico de **0 a 100**, usando pesos e limiares **configuráveis**, e classifica o resultado em uma faixa de risco (baixo/médio/alto). Nesta fase, o cálculo é feito **apenas por regra determinística** (sem modelo de ML paralelo — ver Roadmap). A feature também garante uma restrição estrutural do produto: **nenhum atributo sensível proibido** pode influenciar o cálculo.

Esta feature **não** decide o que fazer com o caso (isso é a feature 2.5 — Roteamento) e **não** coleta os sinais brutos (isso é a feature 2.2 — Coleta de Sinais). Ela é a "caixa de cálculo" entre as duas.

## 2. Problema

Sem um motor de score centralizado, a combinação de sinais de fraude fica implícita, espalhada ou hard-coded — o que impede calibração ágil pela equipe antifraude, dificulta auditoria de por que um caso recebeu determinada pontuação, e cria risco de viés não detectado (uso indireto de atributos sensíveis via sinais proxy).

## 3. Objetivos

- Calcular um score de risco reprodutível e explicável a partir de 3 sinais de entrada fixos.
- Externalizar pesos e limiares como configuração versionada, nunca hard-coded.
- Garantir, por construção, que atributos sensíveis proibidos nunca entram no cálculo.
- Registrar a versão da configuração usada em cada cálculo, para auditoria.
- Tratar sinais ausentes por renormalização de pesos, sem penalizar nem inflar o score indevidamente.
- Alimentar monitoramento de viés do score para compliance.

**Não-objetivos desta feature:** decidir fila/roteamento, coletar sinais brutos, apresentar UI ao analista, executar retreinamento do modelo, introduzir modelo de ML (roadmap).

## 4. Personas

| Persona | Papel nesta feature |
|---|---|
| **Equipe antifraude** | Define/calibra pesos e limiares; consome o histórico de scores para revisão trimestral e gatilho reativo. |
| **Compliance** | Audita o cálculo do score, valida ausência de atributos proibidos, monitora viés. |
| **Motor de Roteamento (2.5)** | Cliente técnico desta feature — consome a faixa de risco calculada para decidir a fila. |
| **Analista de sinistro** | Beneficiário indireto — recebe, via outras features, a justificativa gerada a partir deste cálculo. |

## 5. Jornada (Equipe Antifraude / Compliance)

1. Equipe antifraude define pesos de sinais e limiares de faixa em configuração versionada.
2. Motor calcula scores continuamente para os sinistros que chegam com sinais coletados.
3. Equipe antifraude acompanha métricas (falso positivo/negativo) e, na revisão trimestral ou por gatilho reativo, ajusta a configuração — gerando uma nova versão auditada.
4. Compliance consulta periodicamente o registro de scores para verificar ausência de atributos proibidos e sinais de viés (ex.: correlação entre faixa de risco e proxies geográficos).
5. Quando um sinal está ausente para um sinistro, os pesos dos sinais disponíveis são renormalizados — compliance e antifraude podem consultar quais casos tiveram cobertura parcial.

## 6. Fluxo Completo (com caminho de indisponibilidade)

```
[Sinais coletados do sinistro] (entrada desta feature, vinda da feature 2.2)
[reuso de imagem | IMEI×série | velocity — booleanos]
                │
                ▼
   ┌─────────────────────────────┐
   │ Filtro de atributos proibidos │  ← rejeita/ignora qualquer sinal que
   │ (raça, gênero, religião,      │    corresponda a atributo sensível
   │ orientação sexual, deficiência,│    proibido
   │ idade)                         │
   └───────────────┬──────────────┘
                    │
                    ▼
   ┌─────────────────────────────┐
   │ Serviço de Score DISPONÍVEL?  │
   └───────┬───────────────┬─────┘
       sim │               │ não
           ▼               ▼
┌────────────────────┐   ┌───────────────────────────────┐
│ Motor de Regras      │   │ FAIL-OPEN                      │
│ (pesos configuráveis,│   │ → sem score calculado           │
│ renormalizados se     │   │ → sinaliza "não avaliado por IA"│
│ algum sinal faltar)   │   │ → segue para feature de         │
└──────────┬───────────┘   │   roteamento/fila (2.5), que     │
           ▼               │   trata como caso de revisão     │
┌────────────────────────┐ │   manual                          │
│ Score (0–100)            │ └───────────────────────────────┘
└──────────┬────────────────┘
           ▼
       ┌─────────────────────────────┐
       │ Classificador de Faixa        │
       │ (limiares configuráveis:      │
       │ baixo <30 / médio 30–70 /     │
       │ alto >70)                      │
       └───────────────┬───────────────┘
                        ▼
       ┌─────────────────────────────┐
       │ Registro de Auditoria          │
       │ (sinais usados, cobertura      │
       │ parcial se houver, score,      │
       │ faixa, versão config)          │
       └───────────────┬───────────────┘
                        ▼
        Saída para feature 2.5 (Roteamento)
```

## 7. Regras de Negócio

1. O score combina exatamente 3 sinais booleanos: **reuso de imagem**, **inconsistência IMEI×série** e **velocity alto** (≥2 sinistros do mesmo cliente OU mesmo aparelho/IMEI em janela de 90 dias).
2. Pesos dos sinais e limiares de faixa são **configuráveis** via configuração externa versionada — nunca hard-coded. Pesos-base iniciais (versão v1): reuso de imagem = 50, IMEI×série = 30, velocity = 20 (soma 100).
3. O score é calculado em escala **0–100**. Faixas padrão: **baixo** <30, **médio** 30–70, **alto** >70 (limiares configuráveis).
4. Atributos proibidos (raça/cor, gênero, orientação sexual, religião, deficiência, idade) **nunca** entram como input do cálculo, mesmo que presentes no payload de sinais — devem ser filtrados/ignorados antes do cálculo.
5. Se um sinal estiver ausente, os pesos dos sinais disponíveis são **renormalizados proporcionalmente** para ainda somar 100; o cálculo resultante é marcado como "cobertura parcial" na auditoria.
6. Se o serviço de score estiver indisponível: a feature não calcula score, sinaliza explicitamente "não avaliado" e delega à feature de roteamento o tratamento de fail-open (fila de revisão manual, sinistro segue seu curso).
7. Toda mudança de peso/limiar gera uma nova versão de configuração, auditável, com equipe antifraude como responsável pela calibração (revisão trimestral + gatilho reativo por métrica fora da banda).
8. O cálculo deve ser determinístico e reprodutível: dado o mesmo conjunto de sinais e a mesma versão de configuração, o score e a faixa devem ser sempre os mesmos.
9. Nesta fase, o cálculo é feito apenas por regra determinística — não há modelo de ML paralelo nem lógica de divergência (ver Roadmap Futuro).

## 8. Arquitetura de Alto Nível

```
┌────────────────────────┐
│ Feature 2.2             │
│ Coleta de Sinais         │  (upstream — fora de escopo desta feature)
└───────────┬──────────────┘
            │ sinais do sinistro
            ▼
┌─────────────────────────────────┐
│ FEATURE 2.3 — Score & Regras       │
│                                    │
│  ┌───────────────────────────┐    │
│  │ Filtro de Atributos          │    │
│  │ Proibidos                    │    │
│  └─────────────┬─────────────────┘  │
│                ▼                    │
│  ┌───────────────────────────┐     │
│  │ Motor de Regras (3 sinais,   │◀──┼── Configuração versionada
│  │ pesos configuráveis,          │    │  (pesos, limiares)
│  │ renormaliza se sinal faltar) │     │
│  └─────────────┬─────────────────┘  │
│                ▼                    │
│  ┌───────────────────────────┐     │
│  │ Classificador de Faixa        │    │
│  └─────────────┬─────────────────┘  │
│                │                    │
│                ▼                    │
│  ┌───────────────────────────┐     │
│  │ Publicador de Auditoria       │──┼──▶ Log imutável / Painel Compliance
│  └─────────────┬─────────────────┘  │
└────────────────┼────────────────────┘
                 │ score + faixa + metadados
                 ▼
┌─────────────────────────┐
│ Feature 2.5 — Roteamento  │  (downstream — fora de escopo desta feature)
└─────────────────────────┘
```

## 9. Requisitos Funcionais

| ID | Requisito |
|---|---|
| RF01 | O sistema deve calcular um score numérico (0–100) a partir dos 3 sinais fixos: reuso de imagem, IMEI×série, velocity. |
| RF02 | O sistema deve permitir configurar pesos dos sinais sem alteração de código, com pesos-base v1 de 50/30/20. |
| RF03 | O sistema deve permitir configurar os limiares de faixa (baixo/médio/alto) sem alteração de código, com padrão v1 de <30/30–70/>70. |
| RF04 | O sistema deve classificar o score calculado em uma das três faixas conforme os limiares vigentes. |
| RF05 | O sistema deve filtrar/rejeitar qualquer sinal correspondente a atributo sensível proibido antes do cálculo. |
| RF06 | O sistema deve renormalizar os pesos dos sinais disponíveis quando um sinal estiver ausente, mantendo a soma em 100. |
| RF07 | O sistema deve registrar a versão da configuração (pesos/limiares) usada em cada cálculo, e marcar "cobertura parcial" quando houver renormalização. |
| RF08 | O sistema deve definir o sinal de velocity como ativo quando houver ≥2 sinistros do mesmo cliente OU mesmo aparelho (IMEI) em janela de 90 dias. |
| RF09 | O sistema deve sinalizar explicitamente quando não conseguir calcular o score por indisponibilidade do serviço, sem produzir um score parcial ou estimado. |
| RF10 | O sistema deve ser determinístico: mesma entrada + mesma versão de configuração → mesmo resultado. |
| RF11 | O sistema deve expor os dados necessários para o painel de monitoramento de viés de compliance (distribuição de score por sinal, sem expor atributos proibidos). |

## 10. Requisitos Não Funcionais

- **Assíncrono:** cálculo não bloqueia o fluxo de abertura do sinistro.
- **Latência:** cálculo de score disponível dentro do orçamento de tempo do SLA geral (≤5 min p95 desde a abertura do sinistro, incluindo coleta de sinais).
- **Resiliência:** timeout/circuit breaker no acesso a dependências (ex.: serviço de modelo), com sinalização explícita de indisponibilidade.
- **Configurabilidade:** pesos e limiares externalizados e versionados.
- **Auditabilidade:** todo cálculo gera registro imutável, incluindo casos de cobertura parcial (renormalização).
- **Observabilidade:** métricas de distribuição de score, taxa de cobertura parcial, taxa de indisponibilidade.
- **Reprodutibilidade:** cálculo determinístico por versão de configuração.

## 11. Integrações

- **Feature 2.2 (Coleta de Sinais)** — fonte de entrada dos sinais (nesta fase, pode ser mockada).
- **Serviço de Configuração** — armazena e versiona pesos e limiares.
- **Log/Auditoria** — armazenamento imutável de cada cálculo.
- **Painel de Compliance** — consome dados agregados para monitoramento de viés (fora do escopo de construção desta feature, mas esta feature deve expor os dados).
- **Feature 2.5 (Roteamento)** — consumidora da saída (score + faixa + metadados) desta feature.

## 12. Segurança e LGPD

- Nenhum atributo sensível proibido é utilizado no cálculo, por construção (filtro antes da entrada no motor de regras).
- Minimização: apenas os sinais estritamente necessários ao cálculo entram no motor.
- Mascaramento de dados sensíveis nos logs de auditoria (ex.: não expor foto/EXIF bruto, apenas o resultado do sinal).
- Acesso restrito aos registros de score e à configuração de pesos/limiares (segregação equipe antifraude vs. compliance vs. demais times).
- Base legal: legítimo interesse / prevenção à fraude, documentada — mesma base da coleta de sinais upstream.

## 13. Auditoria

Para cada cálculo de score, registrar de forma imutável:
- Sinais de entrada utilizados (após filtro de atributos proibidos) e sua origem.
- Valor do score calculado (0–100).
- Indicação de "cobertura parcial" quando algum sinal esteve ausente e os pesos foram renormalizados.
- Faixa de risco atribuída.
- Versão da configuração (pesos/limiares) usada no cálculo.
- Timestamp e identificador do sinistro (sem dados pessoais sensíveis no próprio registro de score).

## 14. Casos de Uso

1. **Cálculo padrão com os 3 sinais presentes:** score calculado com pesos-base (50/30/20) sobre reuso de imagem, IMEI×série e velocity.
2. **Cálculo com um sinal ausente:** pesos dos 2 sinais disponíveis são renormalizados para somar 100; caso marcado como "cobertura parcial".
3. **Sinal de velocity ativado:** cliente ou aparelho (IMEI) com ≥2 sinistros em 90 dias → sinal booleano ativo, soma seu peso ao score.
4. **Atributo proibido presente no payload de sinais:** sinal é filtrado antes do cálculo, evento registrado para auditoria de conformidade.
5. **Calibração de peso/limiar pela equipe antifraude:** nova versão de configuração publicada → cálculos subsequentes usam a nova versão; cálculos anteriores mantêm o registro da versão histórica.
6. **Indisponibilidade do serviço de score:** nenhum score é calculado; feature sinaliza explicitamente essa condição para a feature de roteamento tratar como fail-open.

## 15. Casos de Exceção

| Exceção | Tratamento |
|---|---|
| Serviço de cálculo (motor de regras) indisponível | Não calcular score parcial; sinalizar explicitamente "não avaliado" para a feature de roteamento tratar via fail-open. |
| Sinal ausente/parcial | Renormalizar os pesos dos sinais disponíveis para somar 100; marcar o registro como "cobertura parcial". |
| Atributo proibido presente no payload | Filtrar o sinal antes do cálculo; registrar o evento para auditoria de conformidade. |
| Todos os sinais ausentes | Não há score calculável; tratar como equivalente à indisponibilidade (sinalizar "não avaliado"). |
| Configuração de pesos/limiares ausente ou corrompida | Usar o último valor válido conhecido; emitir alerta técnico; nunca operar sem configuração validada. |

## 16. Critérios de Aceite (Gherkin)

```gherkin
Funcionalidade: Cálculo de score e classificação por regras configuráveis

  Cenário: Score é calculado a partir de 2 a 3 sinais de entrada
    Dado um sinistro com sinais de reuso de imagem e inconsistência de IMEI
    Quando o motor de score processa os sinais
    Então um valor numérico de score deve ser produzido
    E esse score deve ser classificado em uma faixa (baixo, médio ou alto)

  Cenário: Pesos e limiares são configuráveis, não hard-coded
    Dado que a equipe antifraude publica uma nova versão de configuração de pesos e limiares
    Quando um sinistro é processado após a publicação
    Então o motor deve usar a nova versão de configuração
    E o registro de auditoria deve indicar essa versão

  Cenário: Atributo sensível proibido nunca entra no cálculo
    Dado um payload de sinais que contém um atributo sensível proibido
    Quando o motor de score processa a entrada
    Então esse atributo deve ser filtrado antes do cálculo
    E o score final não deve refletir esse atributo
    E o evento deve ser registrado para auditoria de conformidade

  Cenário: Sinal ausente aciona renormalização de pesos
    Dado um sinistro com apenas 2 dos 3 sinais disponíveis (reuso de imagem e IMEI×série, sem dado de velocity)
    Quando o motor de score processa a entrada
    Então os pesos dos 2 sinais disponíveis devem ser renormalizados para somar 100
    E o registro de auditoria deve indicar "cobertura parcial"

  Cenário: Indisponibilidade do serviço de score é sinalizada explicitamente
    Dado que o serviço de cálculo de score está indisponível
    Quando um sinistro é processado
    Então nenhum score deve ser calculado ou estimado parcialmente
    E o sistema deve sinalizar explicitamente a condição de "não avaliado"

  Cenário: Cálculo é determinístico
    Dado um conjunto fixo de sinais e uma versão fixa de configuração
    Quando o motor calcula o score duas vezes para essa mesma entrada
    Então o score e a faixa resultantes devem ser idênticos nas duas execuções

  Cenário: Auditoria completa de cada cálculo
    Dado um sinistro processado pelo motor de score
    Quando o cálculo é concluído
    Então o sistema deve registrar os sinais usados, o score, a faixa, a versão da configuração e a indicação de cobertura parcial se houver
    E esse registro deve ser imutável
```

## 17. KPIs

- Precision / recall do score frente a fraudes confirmadas.
- Taxa de cobertura parcial (cálculos com sinal ausente e renormalização).
- Taxa de sinais filtrados por corresponder a atributo proibido (deve tender a zero — indica payload upstream mal higienizado, se ocorrer).
- Taxa de indisponibilidade do serviço de score (impacto no volume de fail-open).
- Distribuição de score por faixa (para acompanhar deriva/calibração ao longo do tempo).

## 18. Riscos

| Risco | Mitigação |
|---|---|
| Limiar de velocity (90 dias / ≥2 sinistros) gera falsos positivos para clientes com histórico legítimo de trocas frequentes | Peso do sinal de velocity é o menor dos três (20); revisão trimestral pode ajustar janela/contagem |
| Configuração de pesos/limiares corrompida derruba o motor | Fallback para última versão válida + alerta técnico imediato |
| Renormalização mascara casos onde múltiplos sinais estão ausentes, gerando score pouco confiável | Marcação obrigatória de "cobertura parcial" na auditoria + métrica de taxa de cobertura parcial monitorada |
| Score deixa de ser determinístico por dependência externa não controlada | Exigir reprodutibilidade como requisito de aceite (RF10) |

## 19. Dependências

- Mecanismo de configuração versionada (arquivo YAML/JSON versionado é suficiente nesta fase) contendo pesos-base v1 (50/30/20) e limiares v1 (<30/30–70/>70).
- Fonte de dados de histórico de sinistros por cliente/aparelho, para calcular o sinal de velocity (janela de 90 dias).

## 20. Itens Fora do Escopo (desta feature)

- Decisão de fila/roteamento do sinistro (feature 2.5).
- Coleta real dos sinais brutos (feature 2.2).
- Tratamento de fila de fail-open (a feature de roteamento trata; esta feature apenas sinaliza indisponibilidade).
- UI do painel do analista e do painel de compliance (esta feature apenas expõe os dados).
- Retreinamento efetivo do modelo a partir do feedback do analista.
- Introdução de modelo de ML paralelo à regra e lógica de detecção de divergência (roadmap futuro).

## 21. Roadmap Futuro

1. Introdução de um modelo de ML paralelo à regra determinística, com detecção e sinalização de divergência regra×modelo (não resolvida automaticamente).
2. Expansão além dos 3 sinais fixos, com pesos por grupo de sinal.
3. Painel de compliance dedicado ao monitoramento de viés, consumindo os dados já expostos por esta feature.
4. Calibração assistida (sugestão automática de novo limiar com base em métricas), mantendo a decisão final humana (equipe antifraude).

## 22. Glossário

| Termo | Definição |
|---|---|
| **Score de risco** | Valor numérico (0–100) resultante da combinação ponderada dos 3 sinais de um sinistro. |
| **Faixa de risco** | Classificação do score em baixo (<30), médio (30–70) ou alto (>70), conforme limiares configurados. |
| **Renormalização** | Redistribuição proporcional dos pesos dos sinais disponíveis quando um sinal está ausente, mantendo a soma em 100. |
| **Velocity** | Sinal booleano ativado quando há ≥2 sinistros do mesmo cliente ou mesmo aparelho (IMEI) em janela de 90 dias. |
| **Fail-open** | Comportamento em que, na indisponibilidade de um serviço, o processo principal continua sem travar; aqui, sem score calculado, sinalizado explicitamente. |
| **Atributo proibido** | Atributo sensível (raça/cor, gênero, orientação sexual, religião, deficiência, idade) que nunca pode influenciar o score. |
| **Configuração versionada** | Conjunto de pesos e limiares externalizado do código, identificado por versão, para permitir calibração auditável. |
