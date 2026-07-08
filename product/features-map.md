# Motor Antifraude de Sinistros — ACME Seguros
## Feature Map

> Visão de produto (problema, público, guardrails, métricas) em [`vision.md`](vision.md).

---

## 2. Feature Map

O mapa está organizado pelas 9 etapas do fluxo (sinistro aberto → coleta de sinais → regras+modelo → score → classificação → roteamento → alertas → decisão do analista → feedback), com features agrupadas por macro-capacidade.

O mapa combina **dois eixos independentes** — não confundir um com o outro:

- **Prioridade de valor** — o selo **[MVP]** marca a fatia sugerida de ~6h (Score + Roteamento por faixa), por ser onde mora o guardrail crítico "nunca bloquear". É uma *recomendação estratégica* (ver §3), não um indicador de entrega.
- **Status de entrega** — quanto de cada capacidade já existe hoje:

  | Marcador | Significado |
  |---|---|
  | ✅ Entregue | Implementado, testado e rodando ponta a ponta |
  | 🔜 Próxima | Próxima fatia do roadmap |
  | 🟡 Parcial | Base já existe na fundação (via mock/esqueleto); falta a versão real |
  | ⏳ Planejado | Ainda não iniciado |

> **Fundação (walking skeleton)** ✅ Entregue — o esqueleto `POST /sinistros → SQS → Worker → MySQL (caso + auditoria)` já roda em Docker, com `IScoreProvider` mock sinalizado. É o que dá a "base 🟡" a várias capacidades abaixo.

### 2.1 Ingestão do Sinistro — ✅ Entregue
- Recepção de evento "sinistro aberto" (Trilha B — por foto)
- Validação de formato mínimo: `idSinistro` é o único campo estrutural; apólice, aparelho, fotos e metadados são opcionais — sua ausência marca o caso como *payload parcial*, nunca gera rejeição
- Enfileiramento assíncrono para o motor antifraude (cliente não espera)

### 2.2 Coleta de Sinais — ⏳ Planejado
- Hash perceptual de imagem (para reuso) **[MVP — se optar pela fatia de imagem]**
- Detecção de edição de imagem (compressão dupla, clonagem, EXIF adulterado)
- Checagem IMEI × número de série × apólice
- Geolocalização vs. histórico do cliente
- Frequência de sinistros por cliente/aparelho (velocity)
- Consulta a base de aparelhos já indenizados
- Extração e análise de metadados EXIF
- Tratamento de sinal ausente/parcial (fallback por sinal)

### 2.3 Score & Regras **[MVP]** — 🔜 Próxima (base 🟡)
> Hoje o `IScoreProvider` é um mock sinalizado; a fatia 1 do roadmap é o **score determinístico real**.
- Motor de combinação de 2–3 sinais em score único **[MVP]**
- Limiares de score configuráveis (não hard-coded) **[MVP]**
- Registro de versão do modelo/regra usada por cálculo **[MVP]**
- Exclusão obrigatória de atributos sensíveis proibidos do cálculo
- Monitoramento de viés do score (painel de compliance)
- Modo de divergência regra × modelo (sinalização, não bloqueio)

### 2.4 Classificação de Risco **[MVP]** — 🟡 Parcial
> Faixa/rota já são atribuídas pela decisão mock na fundação; falta a versão governada por config real.
- Faixas baixo / médio / alto configuráveis **[MVP]**
- Explicação textual da faixa atribuída **[MVP]**

### 2.5 Roteamento **[MVP]** — 🟡 Parcial
> O caminho de fail-open (→ revisão manual) já existe no Worker; falta a fila reforçada por faixa e a saturação.
- Fila normal vs. fila reforçada por faixa de risco **[MVP]**
- Garantia de não-bloqueio: sinistro segue seu curso mesmo na fila reforçada **[MVP]**
- Redistribuição/priorização quando fila reforçada satura
- Caminho de fail-open: IA indisponível → segue + marca para revisão manual **[MVP]**

### 2.6 Geração de Alertas com Justificativa — ⏳ Planejado
- Lista de indícios com evidência individual (ex.: miniatura do hash colidente)
- Justificativa legível, sem linguagem acusatória
- Sugestão de prioridade de análise
- Correlação entre sinistros semelhantes (cross-case linking)

### 2.7 Painel do Analista — ⏳ Planejado
- Fila priorizada por risco
- Cartão do caso: indícios, evidências, score, justificativa
- Ações do analista: aprovar análise reforçada, pedir documentos, encaminhar, marcar falso positivo
- Atalhos para histórico do cliente/aparelho
- Linguagem sem jargão acusatório em toda a UI

### 2.8 Feedback Loop — ⏳ Planejado
- Registro da decisão do analista + justificativa
- Marcação de falso positivo como sinal de recalibração
- Pipeline de retroalimentação ao modelo (com base legal/consentimento documentados)
- Trilha de auditoria da mudança de modelo pós-feedback

### 2.9 Auditoria & Observabilidade (transversal) — 🟡 Parcial
> Auditoria mínima imutável (append-only via trigger) já entregue; falta observabilidade fim a fim e painel de compliance.
- Log estruturado por caso: sinais + origem, score, faixa, versão do modelo/prompt, roteamento, decisão, justificativa **[MVP — versão mínima]**
- Imutabilidade de registros (append-only)
- Observabilidade fim a fim (latência, disponibilidade da IA, filas)
- Painel de compliance: acesso segregado analista × compliance

### 2.10 Segurança & LGPD (transversal) — 🟡 Parcial
> Config por env var e minimização (fotos por referência) já existem; falta controle de acesso e política de retenção formal.
- Base legal documentada (legítimo interesse / prevenção à fraude)
- Minimização e mascaramento de dados sensíveis
- Controle de acesso restrito a dados de fraude
- Política de retenção com prazo definido

### Ferramentas de apoio (transversal, fora da série 2.x)
- **Console de Sinistros** — ⏳ PRD pronto, implementação pendente — cliente web de operação/QA servido pela própria API: envia `POST /sinistros` real e acompanha o ciclo do caso em tela (via `GET /casos/{caseId}` read-only). Não é capacidade antifraude (não coleta sinais, não calcula score, não decide mérito) — é ferramenta de exibição/teste. PRD: [`docs/features/feature-console-sinistros-demo/prd.md`](../docs/features/feature-console-sinistros-demo/prd.md)

---

## 3. Fatia de Valor Recomendada para o MVP (~6h)

> **Nota de estado:** esta é a recomendação estratégica original (decision record da sessão de grilling). Na prática, a **fundação (walking skeleton) + a Feature 2.1 (Ingestão)** foram construídas antes, como habilitadores — não dá para demonstrar Score + Roteamento ponta a ponta sem o esqueleto do pipeline e sem algo alimentando a ingestão. A recomendação abaixo segue válida e aponta a **próxima** fatia de valor (score real, 2.3). Ver o eixo de status na §2.

**Score + Roteamento por faixa**, por ser a fatia que mais expõe o guardrail crítico do produto (nunca bloquear) e a decisão de negócio mais sensível (limiares configuráveis):

1. Receber sinistro com 2–3 sinais simulados/mockados (ex.: reuso de imagem, IMEI divergente, velocity)
2. Combinar os sinais em um score via regra configurável (limiares em arquivo/config, não hard-coded)
3. Classificar em baixo/médio/alto
4. Rotear para fila normal ou reforçada — **sem bloquear o sinistro**
5. Simular fail-open quando o serviço de IA está indisponível
6. Gerar um registro de auditoria mínimo do caso

### Decisões fechadas (sessão de grilling)

| Decisão | Resolução |
|---|---|
| **Fail-open vs. fail-closed** | Fail-open total: sinistro sempre segue seu curso; toda indisponibilidade da IA marca o caso para uma fila de "revisão manual — não avaliado por IA", separada da fila reforçada por risco. |
| **Atributos proibidos no score** | Lista explícita: raça/cor, gênero, orientação sexual, religião, deficiência, idade. Geolocalização só como sinal **comportamental** (distância vs. histórico do próprio cliente) — nunca como "zona de risco" geográfica. |
| **Governança do limiar de risco** | Equipe antifraude é dona do limiar. Revisão trimestral agendada **+** gatilho reativo se falso positivo/negativo sair da banda aceitável. Toda mudança de limiar é um evento de auditoria versionado. |
| **Saturação da fila reforçada** | Sub-priorização dentro da própria fila (score, depois valor do sinistro) + SLA máximo de espera que dispara **alerta operacional** para escalar capacidade — o caso nunca é liberado/bloqueado automaticamente por estouro de SLA. |
| **Base legal do feedback loop** | Mesma finalidade original (legítimo interesse / prevenção à fraude, já documentado). O feedback do analista só entra no retreinamento **agregado/anonimizado**, sem re-expor dados pessoais individuais — evitando configurar nova finalidade de tratamento. |

---

## 4. Fora de Escopo (nesta fatia e nesta iniciativa)

- Decisão de cobertura/indenização (humana)
- Antifraude de contratação (Trilha A)
- Bloqueio de conta
- Interface do cliente final
