using Antifraude.Core.Portas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Antifraude.Infra.Persistencia;

/// <summary>
/// Serviço em background que purga periodicamente as entradas de deduplicação expiradas
/// (> 24h), mantendo a tabela <c>sinistros_processados</c> enxuta. A checagem de idempotência
/// já ignora entradas expiradas; esta purga é só higiene de armazenamento (D2).
/// </summary>
public sealed class PurgaDedupService(
    IServiceScopeFactory scopeFactory,
    ILogger<PurgaDedupService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<ISinistroDedupStore>();
                var removidos = await store.PurgarExpiradosAsync(stoppingToken);
                if (removidos > 0)
                {
                    logger.LogInformation("Purga de dedup: {Removidos} entradas expiradas removidas.", removidos);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Purga é higiene: falha aqui nunca afeta a ingestão.
                logger.LogWarning(ex, "Falha na purga de dedup; tentará no próximo ciclo.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
