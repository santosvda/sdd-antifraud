using Antifraude.Core.Decisao;
using Antifraude.Core.Portas;
using Antifraude.Infra.Mensageria;
using Antifraude.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Antifraude.Worker;

/// <summary>
/// Consumidor assíncrono da fila. Faz long-poll no SQS e, para cada sinistro, roda o
/// <see cref="MotorDeDecisao"/> do Core (resolve config ativa → score → faixa/rota) e
/// persiste caso + auditoria, tudo correlacionado pelo <c>caseId</c>. API e Worker não se
/// chamam direto — só a fila (entrada) e o MySQL (estado).
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
                var motor = sp.GetRequiredService<MotorDeDecisao>();
                var casos = sp.GetRequiredService<ICaseRepository>();
                var auditoria = sp.GetRequiredService<IAuditLog>();

                // O motor nunca lança: sinal faltante/parcial ou queda do provider viram
                // PENDENTE_REVISAO_MANUAL com a causa auditada. O sinistro nunca é bloqueado.
                var resultado = await motor.AvaliarAsync(recebido.Sinistro, ct);

                await casos.SalvarAsync(resultado.Caso, ct);
                await auditoria.RegistrarAsync(resultado.Auditoria, ct);
                await queue.DeleteAsync(recebido.ReceiptHandle, ct);

                logger.LogInformation(
                    "Caso persistido. estado={Estado} faixa={Faixa} rota={Rota} score={Score} versaoConfig={VersaoConfig} versaoProvider={VersaoProvider}",
                    resultado.Caso.Estado, resultado.Caso.Faixa, resultado.Caso.Rota,
                    resultado.Caso.Score, resultado.Caso.VersaoConfig, resultado.Caso.VersaoProvider);
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
