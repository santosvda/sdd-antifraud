using Antifraude.Core.Portas;
using Antifraude.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Antifraude.Infra.Fontes;

/// <summary>
/// Fonte "Histórico de Sinistros" — tabela <c>historico_sinistros</c> alimentada pela
/// própria coleta. Índices por cliente e IMEI atendem o SLA do velocity.
/// </summary>
public sealed class HistoricoDeSinistrosMySql(IDbContextFactory<AntifraudeDbContext> dbFactory)
    : IHistoricoDeSinistros
{
    public async Task<ContagemHistorico> ContarAsync(
        string idSinistroAtual,
        string? idCliente,
        string? imei,
        DateTimeOffset desde,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Invariante: exclui o próprio sinistro — o caso nunca se conta, inclusive
        // quando a mensagem é reprocessada após um registro bem-sucedido.
        var porCliente = string.IsNullOrWhiteSpace(idCliente)
            ? 0
            : await db.HistoricoSinistros.AsNoTracking()
                .CountAsync(h => h.IdCliente == idCliente
                                 && h.AbertoEm >= desde
                                 && h.IdSinistro != idSinistroAtual, ct)
                .ConfigureAwait(false);

        var porAparelho = string.IsNullOrWhiteSpace(imei)
            ? 0
            : await db.HistoricoSinistros.AsNoTracking()
                .CountAsync(h => h.Imei == imei
                                 && h.AbertoEm >= desde
                                 && h.IdSinistro != idSinistroAtual, ct)
                .ConfigureAwait(false);

        return new ContagemHistorico(porCliente, porAparelho);
    }

    public async Task RegistrarAsync(
        string idSinistro,
        string? idCliente,
        string? imei,
        DateTimeOffset abertoEm,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var jaExiste = await db.HistoricoSinistros.AsNoTracking()
            .AnyAsync(h => h.IdSinistro == idSinistro, ct).ConfigureAwait(false);
        if (jaExiste)
        {
            return; // upsert idempotente: reprocessamento não duplica nem infla contagens
        }

        db.HistoricoSinistros.Add(new HistoricoSinistroRegistro
        {
            IdSinistro = idSinistro,
            IdCliente = idCliente,
            Imei = imei,
            AbertoEm = abertoEm,
        });
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Corrida com outro processamento: a PK por id_sinistro garante a idempotência.
        }
    }
}
