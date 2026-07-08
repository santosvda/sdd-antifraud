## ADDED Requirements

### Requirement: Console servido pela própria API
O sistema SHALL servir uma página web estática (vanilla, sem build nem dependências externas) a partir de `wwwroot` da própria API, disponível na raiz (`/`), de modo que o cliente use a mesma origem e não exija CORS.

#### Scenario: Console disponível na raiz
- **WHEN** o ambiente sobe e o usuário abre `http://localhost:8080/`
- **THEN** o Console de Sinistros é exibido, servido pela própria API, sem CORS

### Requirement: Envio real de sinistro por formulário
O Console SHALL montar e enviar um `POST /sinistros` real (mesma origem) a partir de um formulário estruturado com os campos do payload (`idSinistro`, `apolice`, `aparelho.imei/numeroSerie`, `fotos[]`, `metadados.abertoEm/canal/idCliente`), exibindo a resposta recebida sem replicar nem adivinhar a decisão da API.

#### Scenario: Envio válido produz comprovante
- **WHEN** o usuário preenche o formulário e clica em "Enviar sinistro"
- **THEN** o Console dispara o `POST /sinistros` real e exibe o comprovante com o status HTTP e o `caseId` quando 202

### Requirement: Cenários pré-montados
O Console SHALL oferecer cinco cenários como chips de carga rápida que preenchem o formulário: completo e novo, reenvio duplicado, payload parcial, sem `idSinistro` e corpo ilegível.

#### Scenario: Chip carrega cenário
- **WHEN** o usuário aciona um chip de cenário
- **THEN** o formulário é preenchido com o payload correspondente àquele cenário

### Requirement: Idempotência reproduzível nos cenários
O cenário "completo e novo" SHALL gerar um `idSinistro` fresco a cada carga (garantindo PrimeiraVez), e o cenário "duplicado" MUST reusar o `idSinistro` do último envio bem-sucedido da sessão.

#### Scenario: Novo é sempre primeira vez
- **WHEN** o usuário carrega o cenário "completo e novo" e envia
- **THEN** o `idSinistro` é inédito e o resultado é tratado como primeira vez

#### Scenario: Duplicado reusa o último id
- **WHEN** o usuário já enviou um sinistro e aciona o cenário "duplicado"
- **THEN** o Console reenvia o mesmo `idSinistro` do último envio e o resultado evidencia o descarte por idempotência

### Requirement: Cenário de corpo ilegível
O cenário "corpo ilegível" SHALL enviar um corpo cru propositalmente malformado (ignorando o formulário) e MUST exibir aviso de que o envio inválido é intencional.

#### Scenario: Corpo malformado retorna 400
- **WHEN** o usuário aciona o cenário "corpo ilegível" e envia
- **THEN** a API responde 400 e o Console deixa claro que essa é a única rejeição de formato, não uma decisão de fraude

### Requirement: Acompanhamento do ciclo por stepper
O Console SHALL consultar `GET /casos/{caseId}` em polling (cadência ~1s, timeout ~20s) e refletir o progresso num stepper de pipeline, parando cedo nos destinos terminais de ingestão (duplicado → Descartado; sem `idSinistro` → Fila de erro técnico) em vez de esperar um caso que não virá.

#### Scenario: Caso processado acende o stepper
- **WHEN** o Worker processa um sinistro válido
- **THEN** o stepper acende os estágios até "Caso pronto" (PendenteRevisaoManual) e exibe as trilhas de auditoria

#### Scenario: Destino terminal para o polling
- **WHEN** o envio resulta em duplicado descartado ou fila de erro técnico
- **THEN** o Console para o polling de imediato e marca o estágio terminal correspondente

#### Scenario: Timeout sem travar
- **WHEN** o timeout de polling é atingido sem o caso aparecer
- **THEN** o Console exibe "Worker ainda processando — verifique os logs" e permite novo envio

### Requirement: Apresentação sem veredito
O Console SHALL destacar o estado ("aceito — encaminhado para análise humana" / "Pendente de revisão manual") e apresentar faixa, rota e score como sinais de priorização neutros; as cores semânticas (ok/atenção/crítico) MUST ser usadas apenas para a saúde do pipeline, nunca para a faixa de risco, e a tela jamais exibe "fraude/não fraude".

#### Scenario: Resultado não parece veredito
- **WHEN** um caso é exibido
- **THEN** o estado domina a apresentação, faixa/rota/score aparecem como priorização neutra, e nenhuma cor semântica é aplicada à faixa de risco

### Requirement: Histórico da sessão
O Console SHALL manter em memória (client-side) uma lista dos envios da sessão, cada item permitindo reabrir o polling do seu `caseId`, sem persistir nada e sem depender de endpoint de listagem no servidor.

#### Scenario: Envios acumulam no histórico
- **WHEN** o usuário faz vários envios na mesma sessão
- **THEN** cada envio aparece no histórico da sessão e pode ser reaberto pelo seu `caseId`

### Requirement: Detalhes técnicos sob demanda
O Console SHALL oferecer uma seção recolhível de detalhes técnicos exibindo a requisição e as respostas cruas (método/URL, status HTTP, corpos JSON de `POST` e `GET`).

#### Scenario: Expandir detalhes técnicos
- **WHEN** o usuário expande "detalhes técnicos" após um envio
- **THEN** o Console mostra a requisição e as respostas JSON cruas de `POST` e `GET`

### Requirement: Consciência de ambiente e saúde
Ao carregar, o Console SHALL chamar `GET /health` e exibir um indicador de conectividade e um badge de ambiente-alvo; se a API estiver inacessível, MUST exibir um aviso claro orientando o usuário.

#### Scenario: API disponível
- **WHEN** o Console carrega com a API no ar
- **THEN** exibe o indicador de saúde positivo e o badge do ambiente-alvo

#### Scenario: API inacessível
- **WHEN** o Console carrega e a API não responde
- **THEN** exibe aviso claro ("API não encontrada — rode docker compose up") sem quebrar a tela

### Requirement: Minimização e ausência de segredos no cliente
O Console SHALL tratar fotos apenas por referência (URL/ID), sem upload de imagem, e MUST não embutir nenhum segredo, credencial ou dado sensível na página estática.

#### Scenario: Fotos por referência
- **WHEN** o usuário informa fotos no formulário
- **THEN** o Console envia apenas referências (URL/ID), nunca o binário da imagem

### Requirement: Acesso ao Console governado por ambiente
O Console SHALL ser liberado em uso local/dev e MUST exigir token simples quando exposto para acesso compartilhado; se o modo não-local não estiver configurado, o Console fica desabilitado por padrão (default seguro).

#### Scenario: Console desabilitado por padrão fora de local
- **WHEN** a API é exposta em modo não-local sem a configuração de acesso definida
- **THEN** o Console não fica acessível até que a autz seja explicitamente configurada

### Requirement: Piso de qualidade de interface
O Console SHALL ser responsivo até o mobile, oferecer foco de teclado visível, respeitar `prefers-reduced-motion` e evitar scroll horizontal (conteúdo largo rola no próprio contêiner), com contraste legível nos temas claro e escuro.

#### Scenario: Uso em tela estreita e com movimento reduzido
- **WHEN** o Console é aberto em tela estreita e com `prefers-reduced-motion` ativo
- **THEN** o layout empilha sem scroll horizontal e as animações do stepper são suprimidas, mantendo a mudança de estado
