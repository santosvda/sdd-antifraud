## 1. Domínio: contrato e modelo (Core)

- [x] 1.1 Criar constantes dos nomes canônicos dos sinais em `Core/Dominio` (`reuso_imagem`, `imei_serie`, `velocity`) como conjunto fechado esperado
- [x] 1.2 Criar record `ResultadoScore(int? Score, bool CoberturaParcial, IReadOnlyList<string> SinaisUsados, IReadOnlyList<string> SinaisAusentes, string? MotivoNaoAvaliado)` em `Core/Dominio`
- [x] 1.3 Alterar `IScoreProvider.CalcularScoreAsync` para devolver `Task<ResultadoScore>` (BREAKING)
- [x] 1.4 Adicionar `CoberturaParcial` (bool) a `Caso` e a `RegistroAuditoria`

## 2. Motor de regras determinístico (Core)

- [x] 2.1 Criar `FiltroAtributosProibidos` (puro) com blocklist nomeada (raça/cor, gênero, orientação sexual, religião, deficiência, idade) + whitelist fechado dos 3 sinais; devolve sinais aceitos + evento de conformidade quando filtra
- [x] 2.2 Criar `MotorDeRegras` implementando `IScoreProvider`: mapeia entrada para tri-estado por sinal (presente-true/presente-false/ausente), aplica o filtro (2.1)
- [x] 2.3 Implementar piso de cobertura: 0–1 presente → `ResultadoScore` "não avaliado" (Score null + motivo); 2 presentes → renormaliza; 3 presentes → cálculo cheio
- [x] 2.4 Implementar soma booleana ponderada + renormalização proporcional (soma 100) + arredondamento `MidpointRounding.AwayFromZero` + clamp [0,100]; setar `CoberturaParcial` quando renormalizar
- [x] 2.5 Ajustar `MotorDeDecisao`: remover pré-check `SinaisIncompletos`; consumir `ResultadoScore`; mapear `Score is null → FailOpen` (`PendenteRevisaoManual`/`Reforcada`); propagar `CoberturaParcial` para `Caso` e `RegistroAuditoria`
- [x] 2.6 Confirmar `Classificador` com `limiar_medio`/`limiar_alto` da config (baixo<30 / médio / alto>=71); sem hard-code

## 3. Infra: persistência e configuração

- [x] 3.1 Mapear coluna `cobertura_parcial` (bool, default false) em `casos` e `auditoria` no EF
- [x] 3.2 Criar migration: adicionar `cobertura_parcial` nas duas tabelas + inserir `scoring_config` v2 (`reuso_imagem`=50/`imei_serie`=30/`velocity`=20, `limiar_medio`=30, `limiar_alto`=71) e setar v1 `ativa=0`, v2 `ativa=1`
- [x] 3.3 Atualizar o mock `IScoreProvider` (Infra) para o novo retorno `ResultadoScore`, mantendo sinalização de mock (para testes)
- [x] 3.4 Registrar `MotorDeRegras` como `IScoreProvider` no `AddAntifraudeInfra` (caminho real); manter o mock disponível para testes

## 4. Testes

- [x] 4.1 Unit `MotorDeRegras`: 3 sinais true → 100; presente-false soma 0; nome desconhecido descartado
- [x] 4.2 Unit renormalização: 2 presentes → pesos somam 100 + `CoberturaParcial` true; arredondamento determinístico
- [x] 4.3 Unit piso: 0 e 1 sinal → `Score null` + motivo "não avaliado"; nunca fabrica score
- [x] 4.4 Unit `FiltroAtributosProibidos`: atributo proibido filtrado antes do cálculo + evento de conformidade
- [x] 4.5 Unit `Classificador` bordas: 29→baixo, 30→médio, 70→médio, 71→alto
- [x] 4.6 Unit determinismo (RF10): mesma entrada + mesma versão → mesmo score/faixa em duas execuções
- [x] 4.7 Unit `MotorDeDecisao`: `Score null → PendenteRevisaoManual/Reforcada`; cobertura parcial pontua e marca caso+auditoria
- [x] 4.8 Integração worker ponta-a-ponta com config v2: caso com 3 sinais, caso com 2 (cobertura parcial), caso com <2 (não avaliado)

## 5. Fechamento

- [x] 5.1 `dotnet format` + `/simplify` sem pendências; cobertura do `Core` > 80%
- [x] 5.2 Validar ponta-a-ponta no Docker (POST /sinistros com sinais → caso pontuado, faixa e cobertura corretas no MySQL/auditoria)
- [x] 5.3 `openspec validate score-regras` sem erros
