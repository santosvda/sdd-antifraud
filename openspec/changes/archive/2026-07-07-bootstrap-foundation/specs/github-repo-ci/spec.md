## ADDED Requirements

### Requirement: Repositório remoto no GitHub

O projeto SHALL ter um repositório remoto privado no GitHub com branch default `main` (estável e deployável). O fluxo SHALL usar branches short-lived, uma mudança por branch, com commits atômicos.

#### Scenario: Repositório criado e publicado

- **WHEN** a fundação é publicada via `gh repo create acme-antifraude --private --source=. --remote=origin --push`
- **THEN** o repositório existe no GitHub como privado, com `main` como branch default e o código da fundação presente

### Requirement: CI mínimo em cada PR

O sistema SHALL ter um workflow de GitHub Actions que roda `dotnet build` e `dotnet test` a cada pull request. O PR MUST NOT ser considerado apto a merge se build ou testes falharem.

#### Scenario: PR com build e testes verdes

- **WHEN** um PR é aberto contra `main` com código que compila e passa nos testes
- **THEN** o workflow de CI executa `dotnet build` + `dotnet test` e reporta sucesso

#### Scenario: PR com teste quebrado é sinalizado

- **WHEN** um PR é aberto com um teste falhando
- **THEN** o workflow de CI falha e o status do PR reflete a falha
