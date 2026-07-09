# immutable-audit-trail — Delta Spec

## ADDED Requirements

### Requirement: Evidência imutável por sinal coletado

Para cada caso processado, a trilha de auditoria SHALL registrar, por sinal coletado, de
forma imutável (mesma tabela append-only protegida por trigger): o estado do sinal
(ativo / inativo / indisponível), a evidência específica que motivou o valor (ex.:
sinistro colidido e distância de Hamming para reuso de imagem; identificadores comparados
— mascarados — para IMEI×série; contagem e janela para velocity), o motivo da
indisponibilidade quando aplicável (dado ausente × fonte externa inacessível), a origem
do cálculo (ex.: `phash-fake-v1`) e o timestamp do cálculo. Identificadores sensíveis
(IMEI/série) MUST aparecer mascarados na evidência.

#### Scenario: Auditoria registra evidência de cada sinal

- **WHEN** o processamento de um caso pela coleta de sinais é concluído
- **THEN** o registro de auditoria contém, para cada um dos 3 sinais, o estado, a
  evidência, a origem, o timestamp e o motivo de eventual indisponibilidade — e esse
  registro é imutável (UPDATE/DELETE bloqueados)

#### Scenario: Evidência mascara identificadores sensíveis

- **WHEN** a evidência do sinal `imei_serie_divergente` é registrada
- **THEN** IMEI e número de série aparecem truncados/mascarados, nunca in-the-clear
