## ADDED Requirements

### Requirement: Fila de erro técnico para eventos não-processáveis

O sistema SHALL rotear para uma **fila de erro técnico** (DLQ) todo evento que não pode ser
identificado como sinistro — isto é, sem `idSinistro`. Esse roteamento MUST emitir alerta
operacional e MUST NOT devolver erro ao produtor nem afetar o sinistro no sistema principal
(que já existe independentemente). O `idSinistro` é o **único** campo cuja ausência torna o
evento não-processável.

#### Scenario: Evento sem idSinistro vai para a fila de erro técnico

- **WHEN** um evento bem-formado mas sem `idSinistro` é recebido
- **THEN** o evento é roteado para a fila de erro técnico com alerta operacional, e o
  sinistro no sistema principal não é afetado

### Requirement: Retry com backoff exponencial no enfileiramento

O sistema SHALL aplicar retry com backoff exponencial no enfileiramento diante de falha
transitória (ex.: fila de destino indisponível) — **3 tentativas (~1s / 4s / 16s)**. Se a
falha persistir após as tentativas, o evento MUST ser escalado para a fila de erro técnico
com alerta operacional. Nenhum evento MUST ser descartado silenciosamente.

#### Scenario: Falha transitória é superada por retry

- **WHEN** o enfileiramento falha por indisponibilidade momentânea e volta a funcionar dentro
  das 3 tentativas
- **THEN** o evento é enfileirado com sucesso após o retry, sem intervenção manual

#### Scenario: Falha persistente escala para a fila de erro técnico

- **WHEN** o enfileiramento continua falhando após as 3 tentativas com backoff
- **THEN** o evento é roteado para a fila de erro técnico com alerta operacional, sem ser
  descartado
