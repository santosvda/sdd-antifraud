# case-read-api Specification

## Purpose

Endpoint HTTP somente-leitura para consultar um caso e suas trilhas de auditoria (ingestão +
decisão) por `caseId`. Serve o Console de Sinistros (e ferramentas de operação/QA) para fechar
o ciclo assíncrono na tela, sem nunca escrever estado nem expressar decisão de mérito. O acesso
é governado por ambiente (default seguro). É scaffolding de leitura mínima, candidato a ser
absorvido pela futura capacidade do Painel do Analista (Feature 2.7).

## Requirements

### Requirement: Consulta de caso por identificador
O sistema SHALL expor `GET /casos/{caseId}` que retorna, para um `caseId` válido, o caso roteado (estado, faixa, rota, score, versões, `payloadParcial`, `criadoEm`) junto da trilha de ingestão e da trilha de decisão correlacionadas.

#### Scenario: Caso já processado pelo Worker
- **WHEN** um cliente consulta `GET /casos/{caseId}` de um caso já processado
- **THEN** o sistema retorna 200 com o caso, a trilha de ingestão (`auditoria_ingestao`) e a trilha de decisão (`auditoria`) daquele `caseId`

#### Scenario: Evento recebido mas ainda não processado
- **WHEN** um cliente consulta um `caseId` que foi ingerido mas cujo caso ainda não foi persistido pelo Worker
- **THEN** o sistema retorna 200 indicando que o caso ainda não foi processado (`encontrado = false`), incluindo a trilha de ingestão já disponível

#### Scenario: Identificador sem nenhum registro
- **WHEN** um cliente consulta um `caseId` sem caso nem trilhas
- **THEN** o sistema retorna 404 (não encontrado)

### Requirement: Leitura sem efeitos colaterais
O endpoint de consulta SHALL ser estritamente somente-leitura, executando apenas SELECT e nunca escrevendo, atualizando ou removendo registros.

#### Scenario: Consulta não altera estado
- **WHEN** `GET /casos/{caseId}` é chamado qualquer número de vezes
- **THEN** nenhuma linha de `casos`, `auditoria` ou `auditoria_ingestao` é criada, alterada ou removida

### Requirement: Ausência de veredito na resposta
A resposta SHALL refletir apenas o que já foi carimbado internamente (estado + trilhas), sem adicionar nenhum campo de decisão de mérito (aprovado/negado/fraude).

#### Scenario: Resposta não expressa decisão
- **WHEN** um caso é retornado pela consulta
- **THEN** a resposta contém somente os campos auditados do caso e das trilhas, sem nenhum indicador de "fraude", "aprovação" ou "bloqueio"

### Requirement: Acesso governado por ambiente
O acesso ao endpoint SHALL ser liberado em uso local/dev e MUST exigir um token simples quando a API estiver configurada para acesso compartilhado; se o modo não-local não estiver configurado, o endpoint fica indisponível (default seguro).

#### Scenario: Acesso local liberado
- **WHEN** a API roda em modo local/dev e o endpoint é consultado sem token
- **THEN** o sistema responde normalmente

#### Scenario: Acesso compartilhado sem token
- **WHEN** a API está configurada para acesso compartilhado e o endpoint é consultado sem token válido
- **THEN** o sistema recusa o acesso com 401
