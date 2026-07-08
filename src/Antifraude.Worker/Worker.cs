using Antifraude.Core.Coleta;
using Antifraude.Core.Decisao;
using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;
using Antifraude.Infra.Mensageria;
using Antifraude.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Antifraude.Worker;

/// <summary>
/// Consumidor assíncrono da fila. Faz long-poll no SQS e, para cada sinistro, coleta os
/// 3 sinais (feature 2.2, via <see cref="ColetorDeSinais"/> — paralelo, com estado
/// "indisponível" por sinal) e roda o <see cref="MotorDeDecisao"/> do Core (resolve
/// config ativa → score → faixa/rota), persistindo caso + auditoria, tudo correlacionado
/// pelo <c>caseId</c>. API e Worker não se chamam direto — só a fila (entrada) e o MySQL
/// (estado).
///
/// Fail-open vem do motor (nunca lança); aqui garantimos ainda que uma falha de infra
/// numa mensagem não derruba o loop nem some com o sinistro (a mensagem volta à fila).
/// </summary>
public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    ISinistroQueue queue,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await AguardarProntidaoAsync(stoppingToken);
        logger.LogInformation("Worker pronto — consumindo a fila.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var mensagens = await queue.ReceiveAsync(ct: stoppingToken);

                foreach (var recebido in mensagens)
                {
                    await ProcessarAsync(recebido, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Falha no ciclo de recebimento não derruba o worker.
                logger.LogError(ex, "Erro no ciclo de consumo da fila; tentando novamente.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task ProcessarAsync(SinistroRecebido recebido, CancellationToken ct)
    {
        var caseId = recebido.Sinistro.CaseId;
        using (logger.BeginScope(new Dictionary<string, object> { ["caseId"] = caseId }))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sp = scope.ServiceProvider;
                var coletor = sp.GetRequiredService<ColetorDeSinais>();
                var motor = sp.GetRequiredService<MotorDeDecisao>();
                var casos = sp.GetRequiredService<ICaseRepository>();
                var auditoria = sp.GetRequiredService<IAuditLog>();

                // Coleta (2.2): os 3 sinais em paralelo; fonte fora/dado ausente vira
                // sinal "indisponível" — nunca trava o caso nem afeta os outros sinais.
                var sinais = await coletor.ColetarAsync(recebido.Sinistro, ct);
                foreach (var sinal in sinais)
                {
                    logger.LogInformation(
                        "Sinal coletado. sinal={Sinal} estado={Estado} motivo={Motivo} origem={Origem}",
                        sinal.Nome, sinal.Estado, sinal.Motivo, sinal.Origem);
                }

                var sinistro = recebido.Sinistro with { Sinais = sinais };

                // O motor nunca lança: nenhum sinal calculável ou queda do provider viram
                // PENDENTE_REVISAO_MANUAL com a causa auditada. O sinistro nunca é bloqueado.
                var resultado = await motor.AvaliarAsync(sinistro, ct);

                await casos.SalvarAsync(resultado.Caso, ct);
                await auditoria.RegistrarAsync(resultado.Auditoria, ct);
                await queue.DeleteAsync(recebido.ReceiptHandle, ct);

                logger.LogInformation(
                    "Caso persistido. estado={Estado} faixa={Faixa} rota={Rota} score={Score} versaoConfig={VersaoConfig} versaoProvider={VersaoProvider} payloadParcial={PayloadParcial}",
                    resultado.Caso.Estado, resultado.Caso.Faixa, resultado.Caso.Rota,
                    resultado.Caso.Score, resultado.Caso.VersaoConfig, resultado.Caso.VersaoProvider, resultado.Caso.PayloadParcial);
            }
            catch (Exception ex)
            {
                // Falha de infra ao persistir/confirmar: não confirma a mensagem — ela
                // volta à fila para nova tentativa. O sinistro não some.
                logger.LogError(ex, "Falha ao processar sinistro; mensagem retornará à fila.");
            }
        }
    }

    /// <summary>
    /// Aguarda a fundação ficar pronta: fila criada e <c>scoring_config</c> resolvível
    /// (a API aplica migrations + seed). Retry com backoff para tolerar ordem de subida.
    /// </summary>
    private async Task AguardarProntidaoAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sp = scope.ServiceProvider;

                var db = sp.GetRequiredService<AntifraudeDbContext>();
                if (!await db.Database.CanConnectAsync(ct) ||
                    (await db.Database.GetPendingMigrationsAsync(ct)).Any())
                {
                    throw new InvalidOperationException("Banco ainda não migrado.");
                }

                var config = sp.GetRequiredService<IScoringConfigRepository>();
                _ = await config.ObterAtivaAsync(ct);

                await queue.EnsureQueueAsync(ct);
                return;
            }
            catch (Exception ex)
            {
                logger.LogInformation("Aguardando fundação ficar pronta: {Motivo}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }
}
