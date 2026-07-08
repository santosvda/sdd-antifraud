using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antifraude.Core.Coleta;
using Antifraude.Core.Decisao;
using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;
using Antifraude.Infra.Mensageria;
using Microsoft.Extensions.DependencyInjection;

namespace Antifraude.Tests.Integracao;

/// <summary>
/// Reproduz o pipeline do Worker nos testes de integração, sem o BackgroundService:
/// consome a fila, COLETA OS SINAIS (2.2), roda o motor e persiste caso + auditoria.
/// </summary>
internal static class WorkerSimulado
{
    public static async Task<Caso?> ProcessarUmaAsync(IServiceProvider sp, Guid caseId)
    {
        for (var tentativa = 0; tentativa < 5; tentativa++)
        {
            using var scope = sp.CreateScope();
            var s = scope.ServiceProvider;
            var queue = s.GetRequiredService<ISinistroQueue>();
            var coletor = s.GetRequiredService<ColetorDeSinais>();
            var motor = s.GetRequiredService<MotorDeDecisao>();
            var casos = s.GetRequiredService<ICaseRepository>();
            var auditoria = s.GetRequiredService<IAuditLog>();

            foreach (var recebido in await queue.ReceiveAsync())
            {
                var sinais = await coletor.ColetarAsync(recebido.Sinistro);
                var resultado = await motor.AvaliarAsync(recebido.Sinistro with { Sinais = sinais });
                await casos.SalvarAsync(resultado.Caso);
                await auditoria.RegistrarAsync(resultado.Auditoria);
                await queue.DeleteAsync(recebido.ReceiptHandle);
            }

            var caso = await casos.ObterPorIdAsync(caseId);
            if (caso is not null)
            {
                return caso;
            }
        }

        return null;
    }

    public static async Task<(HttpStatusCode Status, Guid CaseId)> PostarAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsJsonAsync("/sinistros", body);
        Guid caseId = default;
        if (resp.StatusCode == HttpStatusCode.Accepted)
        {
            var json = await resp.Content.ReadAsStringAsync();
            caseId = JsonDocument.Parse(json).RootElement.GetProperty("caseId").GetGuid();
        }

        return (resp.StatusCode, caseId);
    }
}
