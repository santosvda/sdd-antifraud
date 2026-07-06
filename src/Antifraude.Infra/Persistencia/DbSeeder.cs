using Antifraude.Core.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antifraude.Infra.Persistencia;

/// <summary>
/// Semeia uma versão ativa inicial da <c>scoring_config</c> (v1) quando a tabela está
/// vazia. Pesos e limiares vivem no banco — nunca hard-coded no caminho de decisão nem
/// em env var; estes valores são apenas o ponto de partida governável.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AntifraudeDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        if (await db.ScoringConfigs.AnyAsync(ct).ConfigureAwait(false))
        {
            return;
        }

        var v1 = new ScoringConfig
        {
            Versao = 1,
            Ativa = true,
            Pesos = new Dictionary<string, double>
            {
                ["reuso_imagem"] = 30,
                ["imei_serie_divergente"] = 25,
                ["geolocalizacao_inconsistente"] = 20,
            },
            LimiarMedio = 30,
            LimiarAlto = 60,
            CriadaEm = DateTimeOffset.UtcNow,
        };

        db.ScoringConfigs.Add(v1);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        logger?.LogInformation("scoring_config v1 semeada (ativa).");
    }
}
