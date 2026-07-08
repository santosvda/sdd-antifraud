# Tasks — coleta-sinais (Feature 2.2)

## 1. Domínio: tri-estado do sinal (Core)

- [x] 1.1 Evoluir `Sinal` para tri-estado: enum `ValorSinal` (Ativo/Inativo/Indisponivel), enum `MotivoIndisponibilidade` (DadoAusente/FonteIndisponivel), campos `Evidencia`, `Motivo`, `CalculadoEm`; remover `Valor` (double)
- [x] 1.2 Redefinir `Sinistro.SinaisIncompletos`: lista vazia OU todos os sinais indisponíveis (unit tests dos dois ramos)
- [x] 1.2b `MotorDeDecisao`: indisponibilidade parcial (1–2 sinais) segue fluxo normal de score com `DadosIncompletos = true`; fail-open total só quando `SinaisIncompletos` (unit tests: parcial com score + marca; 3/3 indisponíveis → `PendenteRevisaoManual`)
- [x] 1.3 Ajustar `MockScoreProvider` ao novo `Sinal` (Ativo→1.0, Inativo→0.0, Indisponivel ignorado) e corrigir testes existentes que constroem `Sinal`

## 2. Portas e calculadores (Core)

- [x] 2.1 Criar portas `IRepositorioDeImagens`, `IBaseDeApolices`, `IHistoricoDeSinistros` em `Core/Portas` (contratos do design D3)
- [x] 2.2 Implementar `CalculadorReusoImagem` (unit: colisão ≤10 ativa com evidência do sinistro colidido + distância; sem colisão inativo; sem foto → DadoAusente sem tocar fonte; fonte lança → FonteIndisponivel)
- [x] 2.3 Implementar `CalculadorImeiSerie` (unit: confere→inativo; diverge→ativo evidência "diverge"; não cadastrado→ativo evidência "não cadastrado"; IMEI mascarado na evidência; sem IMEI/série→DadoAusente; fonte fora→FonteIndisponivel)
- [x] 2.4 Implementar `CalculadorVelocity` (unit: ≥2 em 90d por cliente OU imei→ativo com contagem+janela; <2→inativo; sem idCliente e sem imei→DadoAusente; fonte fora→FonteIndisponivel; sinistro atual não se conta)
- [x] 2.5 Implementar `ColetorDeSinais` com `Task.WhenAll` (unit: exceção de um calculador não escapa nem afeta os demais; saída sempre com os 3 sinais)

## 3. Adapters das fontes (Infra)

- [x] 3.1 Migration: tabelas `imagem_hashes` (índice `criado_em`), `apolices`, `historico_sinistros` (índices `id_cliente`, `imei`) + seed de apólices de exemplo no `DbSeeder` (ramos confere/diverge/não cadastrado)
- [x] 3.2 Adapter `RepositorioDeImagensMySql`: pHash determinístico da referência (origem `phash-fake-v1`), consulta janela 6 meses **excluindo o próprio `idSinistro`**, registro idempotente (upsert) dos hashes do caso atual após o cálculo
- [x] 3.3 Adapter `BaseDeApolicesMySql`: consulta aparelho cadastrado por apólice
- [x] 3.4 Adapter `HistoricoDeSinistrosMySql`: contagens por cliente/imei em 90 dias (a partir de `abertoEm`, fallback data de processamento) **excluindo o próprio `idSinistro`**; upsert idempotente do sinistro atual após o cálculo
- [x] 3.5 Decorator `FonteResiliente` (timeout default 5s + circuit breaker 3 falhas/30s, configuráveis por env var) + env vars `FONTE_*_INDISPONIVEL` de simulação por fonte; registrar tudo no `AddAntifraudeInfra` e no `.env.example`/compose

## 4. Pipeline do Worker

- [x] 4.1 Inserir `ColetorDeSinais` no `Worker.ProcessarAsync` antes do `MotorDeDecisao` (sinistro enriquecido via `with`); log estruturado por sinal (estado, motivo, latência da fonte) correlacionado por `caseId`
- [x] 4.2 Garantir serialização do novo `Sinal` em `auditoria.sinais_json` (estado + evidência + motivo + origem + timestamp), com evidência como objeto JSON estruturado por sinal (design D7: melhor colisão por foto; motivo diverge/não cadastrado; contagem+janela+referência temporal) e IMEI/série mascarados aos últimos 4 dígitos — ajustar mapeamento/conversão se necessário

## 5. Testes de integração (Testcontainers)

- [x] 5.1 Fluxo feliz: payload completo → caso com 3 sinais calculados e evidências na auditoria (`sinais_json`)
- [x] 5.2 Fonte de imagens indisponível (env de simulação) → `reuso_imagem` indisponível (fonte externa), demais calculados; caso segue
- [x] 5.3 Payload parcial sem IMEI → `imei_serie_divergente` indisponível (dado ausente) sem chamada à fonte; `payloadParcial` preservado no caso e auditoria
- [x] 5.4 3 fontes indisponíveis → caso `PENDENTE_REVISAO_MANUAL`, dados incompletos, 3 sinais indisponíveis com motivos na auditoria
- [x] 5.5 Reuso de imagem ponta a ponta: 2 sinistros com a mesma referência de foto → segundo caso com `reuso_imagem` ativo, evidência aponta o sinistro anterior e distância
- [x] 5.6 Velocity ponta a ponta: 2º sinistro do mesmo cliente/imei em <90d → `velocity` ativo com contagem e janela na evidência

## 6. Fechamento

- [x] 6.1 Atualizar `ARCHITECTURE.md` (2.2 existe; nota "sem sinais" sai) e `.env.example` documentando as novas env vars
- [x] 6.2 `dotnet format` + suíte completa verde (unit + integração) + subir via Docker e exercitar cenário de demo (reuso + velocity + fonte fora)
