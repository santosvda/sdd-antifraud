using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;
using Microsoft.EntityFrameworkCore;

namespace Antifraude.Infra.Persistencia;

/// <summary>Adapter EF Core de <see cref="IScoringConfigRepository"/>.</summary>
public sealed class ScoringConfigRepository(AntifraudeDbContext db) : IScoringConfigRepository
{
    public async Task<ScoringConfig> ObterAtivaAsync(CancellationToken ct = default)
    {
        var ativa = await db.ScoringConfigs
            .AsNoTracking()
            .Where(c => c.Ativa)
            .OrderByDescending(c => c.Versao)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return ativa ?? throw new InvalidOperationException(
            "Nenhuma versão ativa de scoring_config encontrada.");
    }
}
