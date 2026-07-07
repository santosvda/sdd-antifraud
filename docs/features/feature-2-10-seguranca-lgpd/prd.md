# PRD — Motor Antifraude de Sinistros
## Feature 2.10: Segurança & LGPD (Transversal)
**ACME Seguros — Seguro de Celular — Trilha B (sinistro por foto)**

---

## 1. Visão Geral

Esta feature é **transversal**, assim como a 2.9 — mas onde a 2.9 formaliza *como* o motor registra e observa o que aconteceu, esta feature formaliza *sob que condições legais e de segurança* ele pode processar dados pessoais em primeiro lugar. Ela consolida em um único contrato o que cada PRD anterior já vinha declarando individualmente: **base legal**, **minimização**, **mascaramento**, **controle de acesso** e **retenção**.

Sem esta feature, cada uma das 9 anteriores teria sua própria interpretação de "o que é minimização suficiente" ou "quem pode acessar o quê" — o que é exatamente o tipo de inconsistência que uma auditoria de compliance ou uma fiscalização da ANPD vai encontrar primeiro. Esta feature é onde essas regras deixam de ser uma frase repetida em cada PRD e se tornam uma **política única, aplicada tecnicamente**, não apenas documentada.

## 2. Problema

Sem uma camada central de segurança e LGPD, o motor antifraude — que por natureza processa dados sensíveis de fraude sobre pessoas identificáveis — corre o risco de: (a) não ter uma base legal formalmente documentada e defensável; (b) mascarar dados de forma inconsistente entre features; (c) não ter uma matriz de acesso clara entre analista, equipe antifraude, compliance e operação; (d) reter dados além do necessário, sem processo formal de expurgo.

## 3. Objetivos

- Consolidar e documentar formalmente a **base legal** (legítimo interesse / prevenção à fraude) usada por todas as features do motor, em um **Relatório de Impacto à Proteção de Dados (RIPD)** único.
- Definir e aplicar tecnicamente as regras de **minimização e mascaramento** de dados sensíveis, de forma consistente entre todas as features.
- Definir a **matriz de controle de acesso** (quem pode ver o quê) entre as personas do produto: analista, equipe antifraude, compliance, operação/SRE.
- Aplicar a **política de retenção** (2 anos, já definida na feature 2.9) de forma uniforme, incluindo o processo de expurgo/anonimização ao final do prazo.
- Servir como ponto único de referência para qualquer auditoria externa ou fiscalização sobre como o motor trata dados pessoais.

**Não-objetivos desta feature:** monitoramento de viés do score (isso pertence à feature 2.3, que já define os atributos proibidos e o monitoramento de viés como responsabilidade própria); implementar a trilha de auditoria em si (feature 2.9); calcular qualquer sinal ou decisão de negócio.

## 4. Personas

| Persona | Papel nesta feature |
|---|---|
| **Compliance / DPO** | Dona do RIPD e da política de retenção; audita a aderência de todas as features. |
| **Todas as features 2.1–2.9** | Consumidoras da política — cada uma já declarou individualmente minimização/mascaramento/base legal em sua própria PRD; esta feature garante consistência. |
| **Analista, Equipe Antifraude, Operação/SRE** | Sujeitos da matriz de controle de acesso — cada papel vê apenas o que sua função exige. |

## 5. Jornada (Compliance / DPO)

1. Antes do motor entrar em produção, compliance/DPO formaliza o RIPD, documentando a base legal, os dados tratados em cada etapa (consolidando o que já está descrito em cada PRD de 2.1 a 2.9) e as medidas de mitigação de risco.
2. A matriz de controle de acesso é aplicada tecnicamente: cada papel (analista, equipe antifraude, compliance, operação) só acessa o que a matriz permite, em qualquer painel do motor (2.7, 2.9).
3. Dados sensíveis são mascarados de forma consistente em todos os pontos onde aparecem (logs, auditoria, painéis) — a mesma regra de mascaramento se aplica independentemente de qual feature gerou o dado.
4. Ao final do prazo de retenção (2 anos, feature 2.9), o processo de expurgo/anonimização é executado de forma automática e auditável.
5. Se uma fiscalização ou auditoria externa perguntar "sob que base legal vocês processam isso, e quem tem acesso a quê", a resposta está pronta no RIPD e na matriz de acesso — não precisa ser reconstruída sob pressão.

## 6. Fluxo Completo (aplicação transversal da política)

```
[Cada feature 2.1–2.9, ao processar dado pessoal]
                │
                ▼
┌─────────────────────────────┐
│ Verificação de Base Legal        │  ← toda feature deve referenciar
│ (RIPD único, legítimo interesse) │    a mesma base legal documentada
└───────────────┬────────────────┘
                ▼
┌─────────────────────────────┐
│ Aplicação de Minimização          │  ← cada feature já define, em sua
│ (catálogo de campos por           │    própria PRD, quais campos
│ finalidade, mantido nesta feature)│    trata; esta feature consolida
└───────────────┬────────────────┘    o catálogo e sinaliza excesso
                ▼
┌─────────────────────────────┐
│ Aplicação de Mascaramento         │  ← regras de mascaramento
│ (regras uniformes por tipo de     │    aplicadas no momento de
│ dado sensível)                    │    exibição/registro, não depois
└───────────────┬────────────────┘
                ▼
┌─────────────────────────────┐
│ Verificação de Controle de Acesso │  ← matriz de acesso aplicada em
│ (matriz analista/antifraude/      │    tempo real a cada consulta,
│ compliance/operação)              │    não apenas por convenção de UI
└───────────────┬────────────────┘
                ▼
┌─────────────────────────────┐
│ Dado tratado/exibido conforme     │
│ a política consolidada             │
└─────────────────────────────┘

  ── Ao final do prazo de retenção (2 anos, apenas dados do motor) ──
[Verificação periódica de idade do dado] → Anonimização irreversível
(registro permanece, sem identificação pessoal) → Registro auditável
do expurgo (feature 2.9, evento de retificação/encerramento)
[Sistemas externos — Sistema de Sinistros, Repositório de Imagens —
seguem sua própria política de retenção, fora deste fluxo]
```

## 7. Regras de Negócio

1. Existe **uma única base legal documentada** (legítimo interesse / prevenção à fraude) referenciada por todas as features do motor — nenhuma feature declara uma base legal própria e divergente.
2. O **catálogo de minimização** — quais campos cada feature trata e por qual finalidade — é mantido de forma centralizada nesta feature, consolidando o que já foi declarado individualmente em cada PRD (2.1 a 2.9). Qualquer campo novo introduzido por uma feature futura deve ser adicionado a este catálogo antes de ir para produção.
3. **Regras de mascaramento** são aplicadas de forma uniforme por tipo de dado sensível: IMEI e número de série são exibidos parcialmente mascarados (ex.: últimos 4 dígitos visíveis) em qualquer painel ou log, exceto no armazenamento funcional interno necessário para o cálculo dos sinais (feature 2.2), que não é exposto diretamente ao usuário. Fotos nunca são expostas na trilha de auditoria além da referência (ID/URL) já definida na feature 2.1.
4. A **matriz de controle de acesso** define, para cada papel, o que é visível:
   - **Analista:** casos atribuídos a ele, painel da feature 2.7.
   - **Equipe antifraude:** métricas de calibração (features 2.3/2.8), configuração de pesos/limiares — sem acesso ao conteúdo individual de sinistros além do necessário para calibração.
   - **Compliance:** trilha consolidada completa e métricas agregadas (feature 2.9), RIPD e catálogo de minimização (esta feature).
   - **Operação/SRE:** dashboards de observabilidade técnica (feature 2.9) — sem acesso a dados pessoais de casos individuais.
5. A política de retenção (2 anos, definida na feature 2.9) se aplica **apenas aos dados que o motor antifraude armazena diretamente**: eventos de auditoria, índice de hashes de imagem (feature 2.2), configurações versionadas e métricas agregadas. Dados em sistemas externos referenciados pelo motor (fotos no Repositório de Imagens, o sinistro em si no Sistema de Sinistros) seguem a política de retenção **daqueles sistemas**, que podem ter requisitos regulatórios próprios — esta feature não sobrepõe uma política de expurgo a um sistema que não é seu.
6. Ao final do prazo de retenção, o processo de expurgo é **anonimização irreversível**, não exclusão física: o registro permanece, preservando valor analítico agregado para calibração de longo prazo, mas sem nenhum identificador que ligue o registro a uma pessoa ou a um sinistro específico. Essa execução gera, ela mesma, um evento auditável (feature 2.9) — o expurgo não é uma operação silenciosa.
7. Nenhuma feature pode contornar a matriz de controle de acesso implementando seu próprio mecanismo de autorização — o controle de acesso é um serviço único, consumido por todas.

## 8. Arquitetura de Alto Nível

```
┌───────┐ ┌───────┐ ┌───────┐ ┌───────┐ ┌───────┐ ┌───────┐ ┌───────┐ ┌───────┐ ┌───────┐
│ 2.1     │ │ 2.2     │ │ 2.3     │ │ 2.4     │ │ 2.5     │ │ 2.6     │ │ 2.7     │ │ 2.8     │ │ 2.9     │
└───┬───┘ └───┬───┘ └───┬───┘ └───┬───┘ └───┬───┘ └───┬───┘ └───┬───┘ └───┬───┘ └───┬───┘
    └─────────┴─────────┴─────────┴─────────┴─────────┴─────────┴─────────┴─────────┘
                                   │ toda operação sobre dado pessoal
                                   ▼
                  ┌──────────────────────────────────────┐
                  │ FEATURE 2.10 — Segurança & LGPD           │
                  │                                          │
                  │  ┌───────────────────────────┐         │
                  │  │ RIPD e Base Legal Única        │        │
                  │  └───────────────────────────┘         │
                  │  ┌───────────────────────────┐         │
                  │  │ Catálogo de Minimização         │       │
                  │  └───────────────────────────┘         │
                  │  ┌───────────────────────────┐         │
                  │  │ Serviço de Mascaramento         │       │
                  │  └───────────────────────────┘         │
                  │  ┌───────────────────────────┐         │
                  │  │ Serviço de Controle de Acesso   │       │
                  │  │ (matriz de papéis)              │       │
                  │  └───────────────────────────┘         │
                  │  ┌───────────────────────────┐         │
                  │  │ Motor de Retenção/Expurgo        │      │
                  │  └───────────────────────────┘         │
                  └──────────────────────────────────────┘
                                   │
                                   ▼
                     Consumido por: painel do analista (2.7),
                     painel de compliance (2.9), toda consulta
                     de dado pessoal em qualquer feature
```

## 9. Requisitos Funcionais

| ID | Requisito |
|---|---|
| RF01 | O sistema deve manter um único documento de base legal (RIPD) referenciado por todas as features. |
| RF02 | O sistema deve manter um catálogo centralizado de minimização, consolidando os campos tratados por cada feature e sua finalidade. |
| RF03 | O sistema deve aplicar mascaramento uniforme de IMEI/número de série (parcial) em qualquer painel ou log, e nunca expor fotos além de sua referência técnica. |
| RF04 | O sistema deve aplicar uma matriz de controle de acesso única, consumida por todos os painéis (2.7, 2.9), impedindo que qualquer feature implemente autorização própria divergente. |
| RF05 | O sistema deve aplicar a política de retenção de 2 anos apenas aos dados que o motor antifraude armazena diretamente (auditoria, hashes, configuração, métricas) — não a sistemas externos referenciados. |
| RF06 | O sistema deve executar, ao final do prazo de retenção, anonimização irreversível (não exclusão física) dos dados sob sua responsabilidade, gerando um evento auditável dessa execução. |
| RF07 | O sistema deve impedir, tecnicamente, acesso de um papel a dados fora do que a matriz de controle de acesso permite. |

## 10. Requisitos Não Funcionais

- **Consistência:** as mesmas regras de mascaramento e controle de acesso se aplicam identicamente em qualquer painel ou log do motor.
- **Auditabilidade:** qualquer execução de expurgo, mudança na matriz de acesso ou atualização do catálogo de minimização é registrada de forma auditável (via feature 2.9).
- **Centralização:** o serviço de controle de acesso e o serviço de mascaramento são pontos únicos de implementação, não duplicados por feature.
- **Disponibilidade:** o serviço de controle de acesso não pode ser um ponto único de falha que bloqueie o uso normal dos painéis — deve ter redundância adequada.

## 11. Integrações

- **Todas as features 2.1–2.9** — consomem o serviço de mascaramento e o serviço de controle de acesso definidos aqui.
- **Feature 2.9 (Auditoria & Observabilidade)** — registra os eventos de expurgo, mudança de matriz de acesso e atualização do catálogo de minimização.
- **Feature 2.7 (Painel do Analista)** — aplica a matriz de controle de acesso para restringir a visão do analista aos próprios casos.
- **Sistema de identidade/autenticação da ACME** — fonte da identidade e papel de cada usuário, consumida pelo serviço de controle de acesso.

## 12. Segurança e LGPD

*(Esta feature É a política de segurança e LGPD do motor — as regras estão detalhadas nas seções 3, 5 e 7 acima. Não há uma subseção adicional separada, para evitar redundância dentro da própria PRD que define o assunto.)*

## 13. Auditoria

Para cada operação relevante desta feature, registrar de forma imutável (via feature 2.9):
- Toda execução de expurgo/anonimização: quais dados, de qual período, quando.
- Toda mudança na matriz de controle de acesso: o que mudou, quem aprovou, quando.
- Toda atualização do catálogo de minimização: campo adicionado/removido, qual feature, finalidade declarada.
- Toda revisão do RIPD: versão, data, responsável.

## 14. Casos de Uso

1. **Auditoria externa pergunta sobre base legal:** compliance apresenta o RIPD único, referenciado por todas as features.
2. **Novo campo é proposto por uma feature futura:** antes de ir para produção, o campo deve ser adicionado ao catálogo de minimização, com finalidade declarada.
3. **Analista tenta acessar um caso não atribuído a ele:** acesso negado pelo serviço de controle de acesso, independentemente de qual painel ele esteja usando.
4. **Prazo de retenção de um lote de eventos de auditoria de 2 anos atrás expira:** anonimização irreversível executada sobre os dados que o motor armazena (o registro permanece, sem identificação), evento auditável gerado; o sinistro em si, no Sistema de Sinistros, segue a política de retenção daquele sistema, não afetada por esta feature.
5. **Compliance verifica se o mascaramento de IMEI está sendo aplicado consistentemente:** consulta qualquer painel ou log e confirma que o padrão (últimos 4 dígitos) é o mesmo em todos os lugares.

## 15. Casos de Exceção

| Exceção | Tratamento |
|---|---|
| Feature nova introduz campo não catalogado | Bloqueado de ir para produção até o campo ser adicionado ao catálogo de minimização com finalidade declarada. |
| Serviço de controle de acesso indisponível | Acesso é negado por padrão (fail-closed) — diferente da filosofia de fail-open do pipeline de negócio, pois aqui o risco de exposição indevida de dados supera o risco de atraso operacional. |
| Expurgo (anonimização) automático falha para um lote de dados | Alerta técnico de severidade alta; nova tentativa agendada; nenhum dado é retido de forma identificável além do prazo sem que isso seja sinalizado. |
| Mascaramento não aplicado corretamente em um painel (bug) | Tratado como incidente de segurança, não como bug funcional comum — escalonamento imediato a compliance. |

## 16. Critérios de Aceite (Gherkin)

```gherkin
Funcionalidade: Política transversal de segurança e LGPD

  Cenário: Todas as features referenciam a mesma base legal
    Dado qualquer feature do motor que processa dado pessoal
    Quando a base legal é consultada
    Então ela deve apontar para o mesmo RIPD único

  Cenário: IMEI é mascarado uniformemente em qualquer painel
    Dado um IMEI exibido em qualquer painel ou log do motor
    Quando o dado é apresentado ao usuário
    Então apenas os últimos 4 dígitos devem estar visíveis, independentemente da feature de origem

  Cenário: Controle de acesso é fail-closed
    Dado que o serviço de controle de acesso está indisponível
    Quando um usuário tenta acessar qualquer painel do motor
    Então o acesso deve ser negado por padrão, nunca liberado por indisponibilidade

  Cenário: Analista não acessa caso de outro analista
    Dado um caso atribuído a outro analista
    Quando um analista tenta acessá-lo pelo painel
    Então o acesso deve ser negado pela matriz de controle de acesso

  Cenário: Expurgo é anonimização irreversível, não exclusão física
    Dado um lote de eventos de auditoria que atingiu o prazo de retenção de 2 anos
    Quando o processo de expurgo é executado
    Então os registros devem permanecer, mas sem nenhum identificador pessoal ou de sinistro
    E um evento auditável deve ser registrado, indicando o que foi anonimizado e quando

  Cenário: Expurgo não se aplica a sistemas externos
    Dado um sinistro cujos dados no motor antifraude atingiram o prazo de retenção
    Quando o expurgo é executado
    Então apenas os dados armazenados diretamente pelo motor devem ser anonimizados
    E os dados no Sistema de Sinistros e no Repositório de Imagens não devem ser afetados por esta feature

  Cenário: Campo não catalogado bloqueia produção
    Dado uma feature nova que introduz um campo de dado pessoal não presente no catálogo de minimização
    Quando essa feature tenta ir para produção
    Então o lançamento deve ser bloqueado até o campo ser catalogado com finalidade declarada
```

## 17. KPIs

- Cobertura do catálogo de minimização (% de campos tratados pelas features que estão devidamente catalogados).
- Taxa de execução bem-sucedida de expurgo automático dentro do prazo.
- Número de incidentes de mascaramento inconsistente detectados.
- Tempo de resposta a uma auditoria externa (indicador indireto de quão pronta está a documentação).

## 18. Riscos

| Risco | Mitigação |
|---|---|
| Feature futura introduz campo sensível sem passar pelo catálogo de minimização | Bloqueio de produção como gate obrigatório (RF02, caso de exceção correspondente) |
| Serviço de controle de acesso centralizado se tornar um ponto único de falha | Redundância na implementação; fail-closed é aceitável para segurança, mas a disponibilidade deve ser alta o suficiente para não gerar atrito operacional constante |
| RIPD desatualizado em relação ao que o motor realmente faz, à medida que novas features são adicionadas | Processo de revisão do RIPD vinculado ao processo de lançamento de qualquer feature nova que toque dado pessoal |

## 19. Dependências

- Elaboração formal do RIPD com jurídico/DPO, incluindo a base legal já usada nas features 2.1–2.9.
- Sistema de identidade/autenticação da ACME, para basear o serviço de controle de acesso nos papéis reais da organização.
- Alinhamento com os donos do Sistema de Sinistros e do Repositório de Imagens sobre suas próprias políticas de retenção, já que este motor não as controla.

## 20. Itens Fora do Escopo (desta feature)

- Monitoramento de viés do score (feature 2.3, responsabilidade própria).
- Implementação da trilha de auditoria em si (feature 2.9 — esta feature define a política que a 2.9 aplica).
- Qualquer cálculo de negócio (score, faixa, roteamento).

## 21. Roadmap Futuro

1. Automação de verificação de conformidade do catálogo de minimização em tempo de deploy (gate de CI/CD), em vez de processo manual de revisão.
2. Painel de compliance dedicado a métricas de aderência LGPD (cobertura de mascaramento, taxa de expurgo, incidentes) — possivelmente integrado ao painel da feature 2.9.
3. Revisão periódica formal do RIPD vinculada ao ciclo de calibração trimestral já estabelecido para o motor.

## 22. Glossário

| Termo | Definição |
|---|---|
| **RIPD** | Relatório de Impacto à Proteção de Dados — documento formal exigido pela LGPD para tratamentos de dados de maior risco, como o antifraude. |
| **Catálogo de minimização** | Registro centralizado de quais campos cada feature trata e por qual finalidade. |
| **Matriz de controle de acesso** | Definição de quais papéis (analista, equipe antifraude, compliance, operação) podem ver quais dados. |
| **Fail-closed** | Comportamento em que, na indisponibilidade de um serviço, o acesso é negado por padrão — oposto do fail-open usado no pipeline de negócio do motor. |
| **Expurgo** | Anonimização irreversível dos dados que o motor antifraude armazena diretamente, ao final do prazo de retenção — o registro permanece, sem identificação pessoal; não se aplica a sistemas externos referenciados pelo motor. |
