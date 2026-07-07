using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;

namespace Antifraude.Infra.Persistencia;

/// <summary>
/// Adapter EF Core de <see cref="IAuditLogIngestao"/>. Só insere na tabela
/// <c>auditoria_ingestao</c> — UPDATE/DELETE são bloqueados por trigger no banco
/// (migration <c>AuditoriaIngestaoImutavel</c>).
/// </summary>
public sealed class AuditLogIngestao(AntifraudeDbContext db) : IAuditLogIngestao
{
    public async Task RegistrarAsync(RegistroIngestao registro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(registro);
        db.AuditoriaIngestao.Add(registro);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
