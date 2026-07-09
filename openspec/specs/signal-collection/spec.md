# signal-collection Specification

## Purpose

Camada de coleta de sinais (feature 2.2): calcula, de forma independente e paralela, os
3 sinais fixos desta fatia — reuso de imagem (pHash 64 bits, Hamming ≤ 10, janela de 6
meses), inconsistência IMEI×série×apólice e velocity (≥2 sinistros do mesmo cliente ou
aparelho em 90 dias) — com tri-estado explícito (ativo / inativo / indisponível),
evidência auditável por sinal e resiliência por fonte de dados (timeout + circuit
breaker independentes). Indisponibilidade nunca trava o caso nem é convertida em "falso".

## Requirements

### Requirement: Cálculo independente e paralelo dos 3 sinais

O sistema SHALL calcular os 3 sinais fixos desta fatia — `reuso_imagem`,
`imei_serie_divergente` e `velocity` — de forma independente e concorrente. A falha,
timeout ou indisponibilidade no cálculo de um sinal MUST NOT impedir o cálculo dos
demais, nem impedir o caso de seguir para a fase de score. Nenhuma exceção de um
calculador SHALL escapar do coletor.

#### Scenario: Falha em uma fonte não impede os demais sinais

- **WHEN** o Repositório de Imagens está indisponível e a Base de Apólices e o Histórico
  de Sinistros estão disponíveis
- **THEN** o sinal `reuso_imagem` é marcado como "indisponível" (motivo: fonte externa) e
  os sinais `imei_serie_divergente` e `velocity` são calculados normalmente

### Requirement: Tri-estado explícito com motivo de indisponibilidade

Cada sinal coletado SHALL ter exatamente um de três estados: **ativo**, **inativo** ou
**indisponível**. Um sinal MUST ser marcado como "indisponível" — nunca como "inativo" —
quando (a) o dado de entrada necessário está ausente no payload, ou (b) a fonte externa
necessária está inacessível. O motivo (`dado ausente` × `fonte indisponível`) MUST ser
registrado. Quando o dado de entrada está ausente, o coletor MUST NOT chamar a fonte
externa correspondente.

#### Scenario: Dado ausente gera sinal indisponível sem tocar a fonte

- **WHEN** um caso marcado como "payload parcial" chega sem IMEI/série
- **THEN** o sinal `imei_serie_divergente` é marcado como "indisponível" com motivo
  "dado ausente", sem nenhuma chamada à Base de Apólices

#### Scenario: Indisponível nunca vira falso

- **WHEN** um sinal não pôde ser calculado por qualquer motivo
- **THEN** seu estado registrado é "indisponível", distinto de "inativo", e é assim que
  ele é publicado para a fase de score

### Requirement: Sinal de reuso de imagem por hash perceptual

O sistema SHALL calcular o sinal `reuso_imagem` comparando o pHash (64 bits) de cada foto
do sinistro atual com o histórico de hashes dos últimos 6 meses, confirmando reuso
(estado ativo) quando a distância de Hamming for ≤ 10. A comparação MUST usar apenas
hashes — nunca bytes de imagem. Enquanto não houver acesso aos bytes reais, o hash MUST
ser derivado de forma determinística da referência da foto e a origem carimbada como fake
(`phash-fake-v1`).

#### Scenario: Colisão de hash dentro do limiar ativa o sinal

- **WHEN** uma foto do sinistro atual tem pHash com distância de Hamming ≤ 10 em relação
  ao hash de um sinistro dos últimos 6 meses
- **THEN** o sinal `reuso_imagem` fica ativo e a evidência indica, por foto colidida, o
  sinistro anterior de menor distância (melhor colisão) e a distância calculada

#### Scenario: Sem colisão o sinal é inativo com evidência

- **WHEN** nenhuma foto do sinistro colide (distância ≤ 10) com o histórico de 6 meses
- **THEN** o sinal `reuso_imagem` fica inativo e a evidência registra quantos hashes
  foram comparados na janela

### Requirement: Sinal de inconsistência IMEI×série×apólice

O sistema SHALL calcular o sinal `imei_serie_divergente` consultando a base de apólices.
O sinal MUST ficar ativo tanto quando o IMEI/série informado **diverge** do cadastrado
quanto quando está **não cadastrado** na apólice; a evidência MUST distinguir os dois
motivos e mascarar identificadores (IMEI/série truncados).

#### Scenario: IMEI não cadastrado ativa o mesmo sinal que IMEI divergente

- **WHEN** o IMEI informado no sinistro não existe em nenhum registro da apólice
- **THEN** o sinal `imei_serie_divergente` fica ativo e a evidência indica
  "não cadastrado", distinto do motivo "diverge"

#### Scenario: IMEI confere com o cadastro

- **WHEN** o IMEI/série informado corresponde ao registrado na apólice
- **THEN** o sinal `imei_serie_divergente` fica inativo e a evidência registra os
  identificadores comparados (mascarados)

### Requirement: Sinal de velocity por janela de 90 dias

O sistema SHALL calcular o sinal `velocity` como ativo quando houver ≥2 sinistros do
mesmo cliente OU do mesmo aparelho (IMEI) na janela de 90 dias, contada a partir de
`abertoEm` quando presente (senão, da data de processamento — a evidência registra qual
referência foi usada). A evidência MUST registrar a contagem encontrada e a janela usada.
O sinistro atual MUST NOT ser contado na própria janela nem colidir consigo mesmo — toda
consulta ao histórico (sinistros e hashes de imagem) MUST excluir o próprio `idSinistro`,
inclusive em reprocessamento de mensagem; o registro do sinistro atual no histórico MUST
ocorrer após o cálculo, como upsert idempotente por `idSinistro`.

#### Scenario: Velocity calculado com contagem e janela corretas

- **WHEN** o histórico tem 2 sinistros do mesmo aparelho nos últimos 90 dias
- **THEN** o sinal `velocity` fica ativo e a evidência indica a contagem e a janela de
  90 dias usada

### Requirement: Resiliência por fonte de dados

O sistema SHALL aplicar timeout e circuit breaker independentes e configuráveis a cada
fonte de dados (repositório de imagens, base de apólices, histórico de sinistros).
Estouro de timeout ou circuito aberto MUST resultar no sinal correspondente marcado como
"indisponível" (motivo: fonte externa), sem retentativas que estourem o orçamento de SLA.
Cada fonte MUST permitir simular indisponibilidade via configuração, para demo e testes.

#### Scenario: Timeout de fonte vira sinal indisponível

- **WHEN** a consulta ao histórico de sinistros excede o timeout configurado
- **THEN** o sinal `velocity` é marcado como "indisponível" (motivo: fonte externa) e o
  caso segue para a fase de score

### Requirement: Publicação do conjunto de sinais para a fase de score

O coletor SHALL publicar o conjunto completo dos 3 sinais (calculados e/ou
indisponíveis), com evidências, para a fase de score, preservando a marca
`payloadParcial` herdada da ingestão. Nenhum sinal SHALL usar atributo sensível proibido,
direta ou indiretamente, no seu cálculo.

#### Scenario: Conjunto completo segue mesmo com indisponibilidades

- **WHEN** a coleta termina com 1 sinal ativo, 1 inativo e 1 indisponível
- **THEN** os 3 sinais seguem para a fase de score com seus estados e evidências, e a
  marca `payloadParcial` original permanece no caso
