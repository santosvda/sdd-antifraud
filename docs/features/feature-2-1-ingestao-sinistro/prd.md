# PRD — Motor Antifraude de Sinistros
## Feature 2.1: Ingestão do Sinistro
**ACME Seguros — Seguro de Celular — Trilha B (sinistro por foto)**

---

## 1. Visão Geral

Esta feature é a **porta de entrada** do motor antifraude: recebe o evento de "sinistro aberto" (Trilha B, por foto) emitido pelo Sistema de Sinistros, valida a presença do payload mínimo necessário para a análise antifraude, e **enfileira assincronamente** o caso para as etapas seguintes (coleta de sinais, score, roteamento). É a feature que materializa o requisito não-funcional mais estrutural do produto: **o cliente nunca espera o antifraude** — o sinistro já foi aberto no sistema principal antes desta feature sequer começar a processar.

Esta feature **não** decide nada sobre o mérito do sinistro, **não** coleta sinais de fraude (isso é a feature 2.2) e **não** pode, em nenhuma circunstância, atrasar ou reverter a abertura do sinistro no sistema principal — sua falha é sempre um problema interno do antifraude, nunca do cliente.

## 2. Problema

Sem um ponto de ingestão bem definido, o antifraude ficaria acoplado de forma síncrona ao fluxo de abertura de sinistro (arriscando lentidão para o cliente honesto) ou dependeria de captura manual/ad hoc de dados (arriscando payloads incompletos, duplicados ou perdidos), prejudicando a qualidade dos sinais que alimentam o score e, no limite, deixando fraudes sem análise por falha silenciosa de dados.

## 3. Objetivos

- Consumir o evento de sinistro aberto (Trilha B) sem impor qualquer latência perceptível ao fluxo do cliente.
- Validar a presença do payload mínimo necessário à análise antifraude.
- Garantir idempotência: o mesmo sinistro nunca é processado em duplicidade pelo motor.
- Enfileirar o caso de forma assíncrona e resiliente para a etapa de coleta de sinais.
- Nunca bloquear, atrasar ou reverter o sinistro no sistema principal, mesmo diante de payload incompleto ou falha interna.
- Deixar rastro auditável da própria ingestão (o quê chegou, quando, e com qual completude).

**Não-objetivos desta feature:** validar mérito do sinistro, coletar ou calcular sinais de fraude, decidir score/roteamento, interagir com o cliente final.

## 4. Personas

| Persona | Papel nesta feature |
|---|---|
| **Sistema de Sinistros** | Produtor do evento "sinistro aberto"; cliente técnico externo a esta feature. |
| **Equipe antifraude** | Consome métricas de completude/latência da ingestão para diagnosticar problemas de dados upstream. |
| **Compliance** | Audita o que foi recebido e retido nesta etapa (minimização de dados). |
| **Feature 2.2 (Coleta de Sinais)** | Consumidora direta da fila alimentada por esta feature. |

## 5. Jornada (Operação / Equipe Antifraude)

1. Sistema de Sinistros publica um evento de "sinistro aberto" (Trilha B) em um canal assíncrono (fila/tópico).
2. Esta feature consome o evento, valida o payload mínimo e verifica se já foi processado antes (idempotência).
3. Se válido (mesmo que parcialmente completo), o caso é enfileirado para a feature 2.2, com uma marca de completude do payload.
4. Se o evento for malformado a ponto de não ser sequer identificável como sinistro (ex.: sem ID de sinistro), vai para uma fila de erro técnico, sem impacto no cliente — o sinistro já existe no sistema principal independentemente disso.
5. Equipe antifraude acompanha, num painel operacional (fora do escopo desta feature construir a UI, mas os dados são expostos), a taxa de payloads incompletos e o tempo de ingestão, para identificar problemas de qualidade de dados upstream.

## 6. Fluxo Completo (com caminho de payload incompleto/inválido)

```
[Sistema de Sinistros]
        │  evento "sinistro aberto" (Trilha B)
        ▼
┌─────────────────────────────┐
│ Consumidor de Evento           │
│ (assíncrono, non-blocking)     │
└───────────────┬────────────────┘
                ▼
┌─────────────────────────────┐
│ Checagem de Idempotência       │  → já processado? descarta com log
│ (chave: ID do sinistro)        │    (não reprocessa, não duplica)
└───────────────┬────────────────┘
                ▼ não processado ainda
┌─────────────────────────────┐
│ Validação de Payload Mínimo    │
│ (apólice, aparelho, fotos,     │
│ metadados)                     │
└───────┬─────────────────┬──────┘
   completo │           │ incompleto/parcial
        ▼                ▼
┌───────────────┐  ┌──────────────────────────┐
│ Enfileiramento  │  │ Enfileiramento marcado     │
│ normal para 2.2 │  │ como "payload parcial" +   │
│                 │  │ segue para 2.2 mesmo assim │
└───────┬─────────┘  └───────────┬────────────────┘
        │                        │
        └───────────┬────────────┘
                     ▼
        ┌─────────────────────────┐
        │ Registro de Auditoria     │
        │ (evento recebido, campos  │
        │ presentes/ausentes,       │
        │ timestamp, ID sinistro)   │
        └───────────┬───────────────┘
                     ▼
          Fila para Feature 2.2
          (Coleta de Sinais)

  ── Caminho de evento malformado (sem ID de sinistro identificável) ──
[Sistema de Sinistros] → evento malformado → Fila de Erro Técnico
                                             (alerta operacional,
                                              não afeta o cliente,
                                              sinistro já existe
                                              no sistema principal)
```

**Ponto crítico do guardrail:** mesmo com payload incompleto, o caso **segue para a fila** (marcado como parcial) em vez de ser descartado — a incompletude é tratada como um sinal de "cobertura parcial" a ser propagado, não como motivo para não analisar o sinistro.

## 7. Regras de Negócio

1. A ingestão é sempre assíncrona: o Sistema de Sinistros não espera resposta desta feature para considerar o sinistro aberto.
2. Payload mínimo esperado: identificador do sinistro, identificador da apólice, identificador do aparelho (IMEI/número de série), referência às fotos enviadas, metadados básicos (data/hora de abertura, canal, identificador do cliente).
3. **Único campo estrutural: o ID do sinistro.** Ausência de qualquer outro campo (apólice, aparelho, fotos, metadados) não impede o enfileiramento — o caso segue marcado como "payload parcial", cabendo às features downstream (2.2 e 2.3) lidar com a ausência do dado.
4. Ausência do ID do sinistro impede a identificação do caso e vai para fila de erro técnico — é evento não processável, não "payload parcial".
5. Idempotência obrigatória: o mesmo identificador de sinistro nunca gera duas entradas na fila para a feature 2.2. O store de deduplicação mantém o ID por **24 horas (TTL)**, cobrindo janelas típicas de retry/reentrega do Sistema de Sinistros.
6. Em nenhuma hipótese esta feature bloqueia, atrasa perceptivelmente ou reverte a abertura do sinistro no sistema principal — falhas aqui são incidentes internos do antifraude.
7. Falha de enfileiramento (ex.: fila indisponível) aciona retry com backoff exponencial curto: **3 tentativas (~1s / 4s / 16s, ~21s total)**; se persistir, vai para fila de erro técnico com alerta operacional — nunca é descartada silenciosamente.
8. Dados de fotos são tratados por referência (ex.: ID/URL no repositório de imagens), nunca replicados/copiados nesta etapa — minimização de dados.

## 8. Arquitetura de Alto Nível

```
┌─────────────────────────┐
│ Sistema de Sinistros      │
└────────────┬───────────────┘
             │ evento "sinistro aberto"
             ▼
┌──────────────────────────────────┐
│ FEATURE 2.1 — Ingestão do Sinistro   │
│                                      │
│  ┌───────────────────────────┐     │
│  │ Consumidor de Evento          │    │
│  └─────────────┬─────────────────┘  │
│                ▼                    │
│  ┌───────────────────────────┐     │
│  │ Verificador de Idempotência   │    │──▶ Store de deduplicação
│  └─────────────┬─────────────────┘  │    (ID do sinistro já visto?)
│                ▼                    │
│  ┌───────────────────────────┐     │
│  │ Validador de Payload Mínimo   │    │
│  └─────────────┬─────────────────┘  │
│                ▼                    │
│  ┌───────────────────────────┐     │
│  │ Publicador de Auditoria       │──┼──▶ Log imutável
│  └─────────────┬─────────────────┘  │
│                ▼                    │
│  ┌───────────────────────────┐     │
│  │ Enfileirador                  │    │
│  └─────────────┬─────────────────┘  │
└────────────────┼────────────────────┘
                 │                     │ evento malformado
                 ▼                     ▼
      ┌─────────────────────┐  ┌─────────────────────┐
      │ Fila → Feature 2.2    │  │ Fila de Erro Técnico  │
      │ (Coleta de Sinais)     │  │ (alerta operacional)  │
      └─────────────────────┘  └─────────────────────┘
```

## 9. Requisitos Funcionais

| ID | Requisito |
|---|---|
| RF01 | O sistema deve consumir o evento de "sinistro aberto" (Trilha B) de forma assíncrona, sem impor espera ao Sistema de Sinistros. |
| RF02 | O sistema deve validar a presença do único campo estrutural — o ID do sinistro — e registrar a presença/ausência dos demais campos do payload mínimo (apólice, aparelho, fotos, metadados) sem bloquear por eles. |
| RF03 | O sistema deve detectar e descartar (com log) eventos duplicados do mesmo sinistro, com base no ID do sinistro, usando um store de deduplicação com TTL de 24 horas. |
| RF04 | O sistema deve enfileirar o caso para a feature 2.2 mesmo quando apólice, aparelho, fotos ou metadados estiverem ausentes, marcando o caso como "payload parcial". |
| RF05 | O sistema deve rotear para fila de erro técnico eventos sem ID de sinistro (não processáveis), sem impacto no cliente. |
| RF06 | O sistema deve reter e retransmitir referências a fotos, nunca cópias/duplicatas dos arquivos de imagem. |
| RF07 | O sistema deve aplicar retry com backoff exponencial (3 tentativas, ~1s/4s/16s) em falhas transitórias de enfileiramento, e escalar para fila de erro técnico se persistirem. |
| RF08 | O sistema deve registrar, para cada evento recebido, quais campos do payload mínimo estavam presentes ou ausentes. |
| RF09 | O sistema não deve, em nenhuma circunstância, emitir chamada síncrona bloqueante de volta ao Sistema de Sinistros. |

## 10. Requisitos Não Funcionais

- **Assíncrono e non-blocking:** zero impacto de latência perceptível ao fluxo de abertura do sinistro.
- **Latência:** ingestão + enfileiramento concluídos em uma fração pequena do orçamento total de SLA (≤5 min p95 ponta a ponta até o alerta ao analista).
- **Idempotência:** garantida por chave de deduplicação (ID do sinistro), com TTL de 24 horas — janela suficiente para cobrir reentregas típicas do produtor.
- **Resiliência:** retry com backoff exponencial (3 tentativas, ~1s/4s/16s), fila de erro técnico para eventos não processáveis, sem perda silenciosa de eventos.
- **Escalabilidade horizontal:** suporta picos de abertura de sinistros sem degradar o SLA.
- **Observabilidade:** métricas de volume, taxa de payload parcial, taxa de duplicidade detectada, taxa de erro técnico.
- **Auditabilidade:** todo evento recebido gera registro imutável de completude e timestamp.

## 11. Integrações

- **Sistema de Sinistros** — produtor do evento de sinistro aberto (Trilha B).
- **Repositório de Imagens** — referenciado (por ID/URL), não acessado diretamente nesta feature.
- **Fila/broker de mensageria** — canal de saída para a feature 2.2 e canal de fila de erro técnico.
- **Store de deduplicação** — para checagem de idempotência (ex.: cache com TTL ou tabela de IDs processados).
- **Log/Auditoria** — armazenamento imutável do registro de ingestão.

## 12. Segurança e LGPD

- Minimização: apenas os campos do payload mínimo necessário à análise antifraude são retidos por esta feature; fotos são referenciadas, não copiadas.
- Base legal: legítimo interesse / prevenção à fraude, documentada — mesma base usada nas demais etapas do motor.
- Mascaramento de dados sensíveis nos logs de auditoria desta etapa (ex.: não logar dados pessoais além dos identificadores necessários).
- Controle de acesso restrito à fila e ao log de ingestão.
- Retenção do registro de ingestão com prazo definido, alinhado à política geral de retenção do motor.

## 13. Auditoria

Para cada evento de sinistro recebido, registrar de forma imutável:
- ID do sinistro, timestamp de recepção.
- Quais campos do payload mínimo estavam presentes/ausentes (flag de "payload parcial" quando aplicável).
- Resultado da checagem de idempotência (processado pela primeira vez / duplicado descartado).
- Destino do roteamento (fila normal de processamento vs. fila de erro técnico).

## 14. Casos de Uso

1. **Evento completo e único:** payload mínimo presente, sem duplicidade → enfileirado normalmente para a feature 2.2.
2. **Evento com payload parcial:** falta algum campo não estrutural (ex.: metadado específico) → enfileirado mesmo assim, marcado como "payload parcial".
3. **Evento duplicado:** mesmo ID de sinistro já processado → descartado com log, sem gerar segunda entrada na fila.
4. **Evento malformado sem ID de sinistro:** não processável → vai para fila de erro técnico, sinistro no sistema principal não é afetado.
5. **Falha transitória de enfileiramento:** retry com backoff; se persistir, escalado para fila de erro técnico com alerta operacional.

## 15. Casos de Exceção

| Exceção | Tratamento |
|---|---|
| Fila/broker de mensageria indisponível | Retry com backoff exponencial (3 tentativas, ~1s/4s/16s); se persistir, escalar para fila de erro técnico com alerta operacional; sinistro no sistema principal não é afetado. |
| Payload sem ID do sinistro | Roteado para fila de erro técnico; único caso tratado como não-processável. |
| Payload sem apólice, aparelho, fotos ou metadados | Enfileirado como "payload parcial" para a feature 2.2 — nenhum desses campos é bloqueante. |
| Evento duplicado (reentrega dentro de 24h) | Detectado por idempotência e descartado, com log; não gera novo processamento. |
| Store de deduplicação indisponível | Evento é processado normalmente (fail-open desta checagem específica), com alerta técnico e registro para reconciliação posterior — nunca bloqueia o enfileiramento por causa da checagem de duplicidade. |

## 16. Critérios de Aceite (Gherkin)

```gherkin
Funcionalidade: Ingestão assíncrona do sinistro para o motor antifraude

  Cenário: Evento de sinistro aberto é enfileirado sem impacto no cliente
    Dado que o Sistema de Sinistros publica um evento de sinistro aberto com payload completo
    Quando a feature de ingestão processa o evento
    Então o caso deve ser enfileirado para a feature de coleta de sinais
    E o Sistema de Sinistros não deve aguardar resposta síncrona desta feature

  Cenário: Payload parcial não impede o enfileiramento
    Dado um evento de sinistro aberto com o ID do sinistro presente, mas sem o ID do aparelho
    Quando a feature de ingestão processa o evento
    Então o caso deve ser enfileirado para a feature de coleta de sinais
    E o caso deve ser marcado como "payload parcial"

  Cenário: Evento sem identificador de sinistro vai para fila de erro técnico
    Dado um evento sem ID de sinistro identificável
    Quando a feature de ingestão processa o evento
    Então o evento deve ser roteado para a fila de erro técnico
    E o sinistro no sistema principal não deve ser afetado

  Cenário: Evento duplicado dentro da janela de 24 horas é descartado sem reprocessamento
    Dado que um evento com o mesmo ID de sinistro já foi processado nas últimas 24 horas
    Quando o mesmo evento é recebido novamente
    Então o evento duplicado deve ser descartado
    E nenhuma nova entrada deve ser criada na fila para a feature de coleta de sinais

  Cenário: Falha de enfileiramento aciona retry e depois escalonamento
    Dado que a fila de destino está temporariamente indisponível
    Quando a feature de ingestão tenta enfileirar um evento válido
    Então o sistema deve tentar novamente até 3 vezes com backoff exponencial (~1s, 4s, 16s)
    E, se a falha persistir após as 3 tentativas, o evento deve ser roteado para a fila de erro técnico com alerta operacional

  Cenário: Auditoria registra a completude de cada evento recebido
    Dado um evento de sinistro processado pela feature de ingestão
    Quando o processamento é concluído
    Então o sistema deve registrar quais campos do payload mínimo estavam presentes ou ausentes
    E esse registro deve ser imutável
```

## 17. KPIs

- Taxa de payload parcial (indicador de qualidade de dados upstream).
- Taxa de duplicidade detectada.
- Taxa de eventos roteados para fila de erro técnico.
- Latência de ingestão (tempo entre publicação do evento e enfileiramento para a feature 2.2).
- Disponibilidade da fila de destino (impacto em retries/escalonamentos).

## 18. Riscos

| Risco | Mitigação |
|---|---|
| Sistema de Sinistros muda o formato do evento sem aviso, quebrando a validação | Validação de schema versionada + alerta técnico imediato em caso de campos inesperados |
| Alto volume de payload parcial mascara problema de integração upstream | Métrica de taxa de payload parcial monitorada, com alerta se sair da banda esperada |
| Store de deduplicação indisponível gera processamento duplicado ocasional | Fail-open controlado (processa mesmo sem checagem) + reconciliação posterior via auditoria |
| Pico de sinistros satura a fila de ingestão | Escalabilidade horizontal do consumidor + backpressure controlado sem descartar eventos |

## 19. Dependências

- Definição do contrato/schema do evento "sinistro aberto" publicado pelo Sistema de Sinistros.
- Infraestrutura de fila/mensageria assíncrona (mesma usada pelas demais features do motor).
- Store de deduplicação (cache com TTL ou tabela) para checagem de idempotência.

## 20. Itens Fora do Escopo (desta feature)

- Coleta de sinais de fraude (feature 2.2).
- Cálculo de score e classificação de risco (feature 2.3).
- Roteamento por fila de risco (feature 2.5).
- Qualquer interação com o cliente final.
- Validação de mérito do sinistro (cobertura, indenização).

## 21. Roadmap Futuro

1. Validação de schema mais rica (ex.: contratos versionados com compatibilidade retroativa formalizada).
2. Painel operacional dedicado à saúde da ingestão (taxa de payload parcial, duplicidade, erro técnico) para a equipe antifraude.
3. Enriquecimento opcional de payload parcial via consulta assíncrona a sistemas complementares (ex.: base de apólices), antes de seguir para a feature 2.2.

## 22. Glossário

| Termo | Definição |
|---|---|
| **Payload mínimo** | Conjunto de campos considerados indispensáveis para que o caso siga para análise antifraude. |
| **Payload parcial** | Situação em que o payload mínimo estrutural está presente, mas algum campo não estrutural está ausente. |
| **Idempotência** | Propriedade que garante que o reprocessamento do mesmo evento não gera efeitos duplicados. |
| **Fila de erro técnico** | Canal de destino para eventos não processáveis (sem identificação do sinistro), usado para alerta operacional sem impacto no cliente. |
| **Non-blocking** | Característica de um processo que não impõe espera síncrona a quem o invoca. |
