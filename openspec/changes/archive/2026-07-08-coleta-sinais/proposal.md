# Proposal — coleta-sinais (Feature 2.2)

## Why

Hoje todo sinistro chega ao `MotorDeDecisao` **sem sinais** e cai no fail-open
(`PendenteRevisaoManual`) — o motor de score da feature 2.3 não tem insumo real para
trabalhar. A feature 2.2 (PRD em `docs/features/feature-2-2-coleta-sinais/prd.md`) cria a
camada de coleta: calcula os 3 sinais fixos desta fatia — **reuso de imagem** (pHash 64
bits, Hamming ≤ 10, janela de 6 meses), **inconsistência IMEI×série×apólice** e
**velocity** (≥2 sinistros do mesmo cliente OU aparelho em 90 dias) — de forma
independente e paralela, com estado explícito de **"indisponível"** (nunca convertido em
"falso") e **evidência auditável** por sinal.

## What Changes

- Novo modelo de domínio de sinal coletado no `Core`: tri-estado
  (ativo / inativo / **indisponível**), evidência específica por sinal e motivo de
  indisponibilidade (dado ausente no payload × fonte externa inacessível). **BREAKING**
  (interno): o record `Sinal(Nome, Valor, Origem)` evolui para comportar o tri-estado — o
  `MotorDeDecisao` e o `MockScoreProvider` são ajustados.
- Novo `ColetorDeSinais` no `Core`: orquestra os 3 calculadores **em paralelo**, isola
  falhas (a queda de uma fonte afeta só o sinal correspondente) e agrega o conjunto de
  sinais com evidências.
- 3 novas portas no `Core` (uma por fonte de dados): repositório de imagens (hashes
  pHash), base de apólices (IMEI/série cadastrado) e histórico de sinistros (contagem por
  cliente/aparelho). Adapters em `Infra` com timeout e circuit breaker independentes por
  fonte, incluindo modo de indisponibilidade simulável (mesmo padrão do
  `MockScoreProvider`).
- Worker passa a executar a coleta de sinais **antes** do `MotorDeDecisao`: o `Sinistro`
  segue para o motor com os sinais populados (calculados e/ou indisponíveis), preservando
  a marca `payloadParcial` herdada da 2.1.
- Auditoria do caso passa a registrar, de forma imutável, valor + evidência + motivo de
  indisponibilidade de cada um dos 3 sinais (coluna `sinais_json` da tabela `auditoria`).
- Logs/métricas de disponibilidade por sinal (taxa indisponível, latência por fonte) via
  logs estruturados existentes.

## Capabilities

### New Capabilities

- `signal-collection`: cálculo independente e paralelo dos 3 sinais fixos (reuso de
  imagem, IMEI×série×apólice, velocity), com tri-estado, evidência por sinal, motivo de
  indisponibilidade e resiliência por fonte (timeout/circuit breaker); nenhum sinal usa
  atributo sensível proibido.

### Modified Capabilities

- `claim-processing-worker`: o requirement "Consumo do payload de sinistro real sem
  sinais computados" é substituído — o Worker agora coleta os sinais antes de invocar o
  motor; a ausência total de sinais calculáveis (3× indisponível) continua fail-open.
- `immutable-audit-trail`: a trilha de auditoria do caso passa a exigir o registro
  imutável do valor, evidência e motivo de indisponibilidade de cada sinal coletado.

## Impact

- **Core**: `Dominio/Sinal.cs` (evolui), novo `Coleta/` (ColetorDeSinais + 3
  calculadores), novas portas em `Portas/`; `MotorDeDecisao`/`Classificador` seguem
  intactos na lógica de score (2.3 fica fora de escopo), apenas o tipo de sinal muda.
- **Infra**: novos adapters das 3 fontes (fakes locais persistidos no MySQL +
  simulação de indisponibilidade via env var), migration para armazenamento de hashes
  pHash com janela de 6 meses e índice de histórico por cliente/IMEI.
- **Worker**: pipeline ganha o passo de coleta antes do motor.
- **API**: sem mudança de contrato (`POST /sinistros` inalterado).
- **Testes**: unit (calculadores, coletor, tri-estado, evidência) + integração
  (fonte fora do ar → sinal indisponível, demais calculados).
- **Fora de escopo**: score/faixa (2.3), roteamento por fila (2.5), demais sinais do
  catálogo (EXIF, edição de imagem, geolocalização, aparelho já indenizado).
