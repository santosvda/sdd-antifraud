using Antifraude.Core.Portas;
using Microsoft.EntityFrameworkCore;

namespace Antifraude.Infra.Persistencia;

/// <summary>
/// Adapter EF Core de <see cref="ISinistroDedupStore"/>. Store de deduplicação em tabela
/// MySQL (<c>sinistros_processados</c>) com TTL lógico de 24h: um id só conta como "visto" se
/// registrado dentro da janela. O fail-open (processar mesmo com o store fora) é
/// responsabilidade de quem chama; aqui só encapsulamos a persistência.
/// </summary>
public sealed class SinistroDedupStore(AntifraudeDbContext db, TimeProvider clock) : ISinistroDedupStore
{
    /// <summary>Janela de deduplicação — cobre reentrega/retry típicos do produtor.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task<bool> RegistrarSeNovoAsync(string idSinistro, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idSinistro);

        var agora = clock.GetUtcNow();
        var limite = agora - Ttl;

        var existente = await db.SinistrosProcessados
            .FirstOrDefaultAsync(x => x.IdSinistro == idSinistro, ct)
            .ConfigureAwait(false);

        if (existente is null)
        {
            db.SinistrosProcessados.Add(new SinistroProcessado { IdSinistro = idSinistro, PrimeiraVezEm = agora });
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return true; // primeira vez
            }
            catch (DbUpdateException)
            {
                // Corrida: outra requisição inseriu o mesmo id — trata como duplicado.
                db.Entry(db.SinistrosProcessados.Local.First(x => x.IdSinistro == idSinistro)).State = EntityState.Detached;
                return false;
            }
        }

        if (existente.PrimeiraVezEm <= limite)
        {
            // Entrada expirada: renova a janela e trata como novo.
            existente.PrimeiraVezEm = agora;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }

        return false; // duplicado dentro da janela
    }

    public async Task<int> PurgarExpiradosAsync(CancellationToken ct = default)
    {
        var limite = clock.GetUtcNow() - Ttl;
        return await db.SinistrosProcessados
            .Where(x => x.PrimeiraVezEm <= limite)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
