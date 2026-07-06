using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;
using Microsoft.EntityFrameworkCore;

namespace Antifraude.Infra.Persistencia;

/// <summary>Adapter EF Core de <see cref="ICaseRepository"/>.</summary>
public sealed class CaseRepository(AntifraudeDbContext db) : ICaseRepository
{
    public async Task SalvarAsync(Caso caso, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caso);
        db.Casos.Add(caso);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<Caso?> ObterPorIdAsync(Guid caseId, CancellationToken ct = default) =>
        await db.Casos.AsNoTracking().FirstOrDefaultAsync(c => c.CaseId == caseId, ct).ConfigureAwait(false);
}
