using Antifraude.Core.Dominio;

namespace Antifraude.Core.Portas;

/// <summary>Porta de acesso à <c>scoring_config</c> versionada.</summary>
public interface IScoringConfigRepository
{
    /// <summary>Resolve a versão ativa no momento do cálculo. Lança se não houver ativa.</summary>
    Task<ScoringConfig> ObterAtivaAsync(CancellationToken ct = default);
}
