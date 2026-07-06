using Antifraude.Core.Dominio;

namespace Antifraude.Core.Portas;

/// <summary>Porta de persistência do caso roteado.</summary>
public interface ICaseRepository
{
    Task SalvarAsync(Caso caso, CancellationToken ct = default);

    Task<Caso?> ObterPorIdAsync(Guid caseId, CancellationToken ct = default);
}
