using Antifraude.Core.Dominio;

namespace Antifraude.Core.Portas;

/// <summary>
/// Porta da trilha de auditoria da ingestão. Só escreve — a alteração/remoção é bloqueada
/// por trigger no banco (mesma disciplina append-only da <see cref="IAuditLog"/>).
/// </summary>
public interface IAuditLogIngestao
{
    Task RegistrarAsync(RegistroIngestao registro, CancellationToken ct = default);
}
