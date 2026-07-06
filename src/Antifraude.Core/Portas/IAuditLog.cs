using Antifraude.Core.Dominio;

namespace Antifraude.Core.Portas;

/// <summary>
/// Porta da trilha de auditoria append-only. Só escreve — a leitura/alteração é
/// bloqueada por trigger no banco. Toda decisão passa por aqui.
/// </summary>
public interface IAuditLog
{
    Task RegistrarAsync(RegistroAuditoria registro, CancellationToken ct = default);
}
