## ADDED Requirements

### Requirement: Limiares configuráveis versionados no banco

O sistema SHALL armazenar a configuração de scoring (pesos dos sinais e limiares de faixa baixo/médio/alto) em uma tabela `scoring_config` versionada no MySQL. Esses valores MUST NOT ser hard-coded no código nem lidos de variáveis de ambiente.

#### Scenario: Configuração vive no banco, não no código

- **WHEN** um caso é processado
- **THEN** os pesos e limiares usados vêm de uma linha da tabela `scoring_config`, não de constantes de código nem de env vars

### Requirement: Versão ativa resolvida e carimbada no caso

O processamento SHALL resolver a versão ativa da `scoring_config` no momento do cálculo e **carimbar essa versão no caso** (rastreável como "o score veio da config vN").

#### Scenario: Caso aponta a versão da config que o originou

- **WHEN** o Worker calcula o score de um sinistro usando a config ativa v3
- **THEN** o caso persistido e sua auditoria registram que o resultado veio da `scoring_config` v3

#### Scenario: Nova versão não reescreve casos anteriores

- **WHEN** uma nova versão da `scoring_config` (v4) passa a ser a ativa
- **THEN** casos criados sob a v3 continuam carimbados com v3, e novos casos passam a carimbar v4
