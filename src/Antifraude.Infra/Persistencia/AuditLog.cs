using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;

namespace Antifraude.Infra.Persistencia;

/// <summary>
/// Adapter EF Core de <see cref="IAuditLog"/>. Só insere — qualquer UPDATE/DELETE é
/// bloqueado por trigger no banco (ver migration <c>AuditoriaImutavel</c>).
/// </summary>
public sealed class AuditLog(AntifraudeDbContext db) : IAuditLog
{
    public async Task RegistrarAsync(RegistroAuditoria registro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(registro);
        db.Auditoria.Add(registro);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
