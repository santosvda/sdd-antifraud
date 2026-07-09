## 1. Domínio — tipos novos no Core (aditivo)

- [x] 1.1 Criar o enum `MotivoSemClassificacao` no Core (`SinalAusente`, `ProviderIndisponivel`, `ConfigIndisponivel`, `ScoreForaDeFaixa`, `ConfigCorrompida`), com um separador claro entre indisponibilidade esperada e anomalia (ex.: método/extensão `EhAnomalia`).
- [x] 1.2 Adicionar campos `Explicacao` (string?), `VersaoTemplate` (string?) e `Motivo` (`MotivoSemClassificacao?`) à entidade `Caso`.
- [x] 1.3 Adicionar os mesmos campos (`Explicacao`, `VersaoTemplate`, `Motivo`) ao `RegistroAuditoria`.
- [x] 1.4 Estender `ResultadoDecisao` para carregar a explicação e o motivo (sem quebrar consumidores atuais).

## 2. Porta de alerta técnico

- [x] 2.1 Definir a porta `IAlertaTecnico` no Core (`EmitirAsync(severidade, codigo, contexto/caseId, ct)`), com um enum de severidade.
- [x] 2.2 Implementar o adapter na Infra que emite log estruturado nível Critical com `caseId` e código da anomalia.
- [x] 2.3 Registrar a porta na composição de DI (`AddAntifraudeInfra`).

## 3. Módulo de template e classificação (Core, puro)

- [x] 3.1 Criar o módulo de template com a constante `VersaoTemplate` e os textos determinísticos (faixa classificada, cobertura parcial, rótulos canônicos por motivo).
- [x] 3.2 Criar o mapa em código `id-técnico → nome-de-exibição` dos sinais, com fallback seguro para sinal desconhecido (nunca vaza o id cru).
- [x] 3.3 Implementar o `GeradorDeExplicacao` puro: recebe score, faixa, sinais e `coberturaParcial`; nomeia sinais ativados (`Valor > 0`) em linguagem de indício; menciona cobertura parcial quando `true`; é determinístico.
- [x] 3.4 Implementar a produção do rótulo canônico não-acusatório a partir do `MotivoSemClassificacao` (fonte única no Core).
- [x] 3.5 Estender o `Classificador` (ou tipo 2.4 dedicado) para detectar score fora de `[0,100]` e devolver o motivo `ScoreForaDeFaixa` em vez de classificar.

## 4. Seam mínimo no pipeline (combinar com o dev da 2.3)

- [x] 4.1 Remover o `Math.Clamp(score, 0, 100)` do `MotorDeDecisao`; delegar out-of-range à detecção de anomalia (task 3.5).
- [x] 4.2 No caminho classificado, invocar o `GeradorDeExplicacao` e preencher `Explicacao`/`VersaoTemplate` no `Caso` e no `RegistroAuditoria`.
- [x] 4.3 Nos caminhos sem classificação (fail-open e anomalia), carimbar o `MotivoSemClassificacao` + rótulo canônico; explicação de faixa fica `null`.
- [x] 4.4 Emitir `IAlertaTecnico` (severidade alta) quando o motivo for anomalia (`ScoreForaDeFaixa`, `ConfigCorrompida`, `ConfigIndisponivel`); não emitir para indisponibilidade esperada.
- [x] 4.5 No branch de config não resolvível, marcar motivo `ConfigIndisponivel`/`ConfigCorrompida` + alerta, sem implementar fallback de versão (responsabilidade da 2.3).

## 5. Persistência e migration

- [x] 5.1 Mapear os campos novos (`Explicacao`, `VersaoTemplate`, `Motivo`) no `AntifraudeDbContext` para `casos` e `auditoria`.
- [x] 5.2 Gerar a migration EF aditiva (colunas nullable) e conferir que ela não recria/afrouxa o trigger de imutabilidade da auditoria.
- [x] 5.3 Verificar que a migration aplica no start da API e que INSERT continua permitido enquanto UPDATE/DELETE seguem bloqueados na auditoria.

## 6. Testes

- [x] 6.1 Unit do `GeradorDeExplicacao`: faixa classificada nomeia sinais ativados em linguagem de indício; fallback de sinal desconhecido; menção de cobertura parcial (true/false); determinismo (mesma entrada+versões → mesmo texto).
- [x] 6.2 Unit do classificador: limites fechado-aberto (30→médio, 70→alto); score fora de `[0,100]` → motivo `ScoreForaDeFaixa` sem coação.
- [x] 6.3 Unit dos rótulos canônicos por `MotivoSemClassificacao` (não-acusatórios, sem faixa inventada em fail-open).
- [x] 6.4 Criar um `IScoreProvider` de teste no projeto Tests (injeta sinais e scores fora de faixa) — sem tocar o `MockScoreProvider` compartilhado.
- [x] 6.5 Integração: pipeline classifica com sinais sintéticos e persiste explicação/versões no caso e na auditoria (imutável).
- [x] 6.6 Integração: score fora de faixa → caso sem classificação com motivo `ScoreForaDeFaixa` + alerta técnico severidade alta emitido; sinistro segue.
- [x] 6.7 Integração: fail-open esperado (provider indisponível) carimba motivo, não emite alerta técnico.

## 7. Fechamento

- [x] 7.1 Rodar `dotnet format` e `/simplify` nas mudanças (format verde; /simplify aplicou: consolidou test-doubles, enxugou params do `Montar`, simplificou `Juntar`).
- [x] 7.2 Rodar a suíte completa (unit + integração via Testcontainers) verde — 49 unit + 19 integração, todos verdes.
- [x] 7.3 `openspec validate classificacao-risco-2-4` verde. (Revisão do diff do seam com o dev da 2.3 antes do merge fica como passo humano de coordenação.)
