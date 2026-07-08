# Motor Antifraude de Sinistros — ACME Seguros
## Visão de Produto

> Mapa de capacidades e roadmap em [`features-map.md`](features-map.md).

---

### 1.1 Declaração de Visão

> Para analistas de sinistro e equipes antifraude da ACME Seguros, que precisam identificar indícios de fraude em sinistros de celular sem travar o atendimento ao cliente honesto, o **Motor Antifraude de Sinistros** é um sistema de suporte à decisão que coleta sinais, calcula um score de risco explicável e prioriza casos para análise humana — diferente de soluções de bloqueio automático, o motor **nunca decide**, apenas **instrumenta o analista** com evidência e contexto para decidir melhor e mais rápido.

### 1.2 Problema

Sinistros fraudulentos (imagens reaproveitadas, aparelhos já indenizados, inconsistências de IMEI/apólice) hoje passam despercebidos ou dependem inteiramente do faro do analista, sem sinalização sistemática de indícios, sem histórico cruzado entre sinistros e sem priorização de fila — o que aumenta a perda (loss ratio) e sobrecarrega a análise humana com casos de baixo risco enquanto casos de alto risco esperam na mesma fila.

### 1.3 Para quem

- **Analista de sinistro** — usuário primário, opera o painel, decide o caso.
- **Equipe antifraude** — calibra limiares, investiga padrões, audita casos.
- **Compliance** — audita trilha, decisão e base legal (LGPD).
- **Cliente honesto** — beneficiário indireto: não deve sentir atrito adicional.

### 1.4 O que o produto NÃO é

- Não é um sistema de aprovação/negação automática de sinistro.
- Não decide cobertura ou indenização.
- Não faz antifraude de contratação (Trilha A).
- Não bloqueia contas ou clientes.
- Não é a interface do cliente final — é 100% backoffice.

### 1.5 Princípios inegociáveis (guardrails)

| Princípio | Implicação de produto |
|---|---|
| Human-in-the-loop obrigatório | Toda ação sobre o caso é do analista; o motor só alerta |
| Nunca bloqueio automático | Score alto → fila reforçada, nunca recusa |
| Fail-open de IA | IA indisponível → sinistro segue seu curso normal + revisão manual |
| Não-acusação | Linguagem de "indício"/"hipótese", nunca "veredito" |
| Não-discriminação | Atributos sensíveis proibidos fora do score; viés monitorado |
| Auditoria imutável | Sinais, score, versão do modelo, decisão — tudo rastreável |
| LGPD by design | Minimização, base legal documentada, retenção definida |

### 1.6 Métricas de sucesso (North Star + apoio)

- **North Star:** Perda evitada (R$) por fraude detectada corretamente, sem aumento de atrito ao cliente honesto.
- **Apoio:**
  - Precision / recall do score
  - Taxa de falso positivo / falso negativo
  - Tempo médio de análise por caso
  - % de casos revisados dentro do SLA (≤5 min p95 para alerta)
  - Taxa de concordância analista × modelo (calibração do feedback loop)
