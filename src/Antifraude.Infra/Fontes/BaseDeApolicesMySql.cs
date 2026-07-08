using Antifraude.Core.Portas;
using Antifraude.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Antifraude.Infra.Fontes;

/// <summary>
/// Fonte "Base de Apólices" — fake local persistido no MySQL (tabela <c>apolices</c>,
/// semeada pelo <see cref="DbSeeder"/> com os ramos confere/diverge/não cadastrado).
/// </summary>
public sealed class BaseDeApolicesMySql(IDbContextFactory<AntifraudeDbContext> dbFactory) : IBaseDeApolices
{
    public async Task<AparelhoCadastrado?> ObterAparelhoCadastradoAsync(
        string apolice, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var registro = await db.Apolices.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Apolice == apolice, ct).ConfigureAwait(false);

        return registro is null ? null : new AparelhoCadastrado(registro.Imei, registro.NumeroSerie);
    }
}
