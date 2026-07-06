using Antifraude.Core.Dominio;

namespace Antifraude.Core.Portas;

/// <summary>
/// Porta de cálculo de score. Na fundação é um mock explícito e sinalizado; o motor
/// determinístico (fatia 1) e o ML (roadmap) entram atrás desta mesma interface sem
/// tocar no resto. Nenhum score é fabricado fora desta porta.
/// </summary>
public interface IScoreProvider
{
    /// <summary>Versão/sinalização do provider, carimbada na auditoria (ex.: <c>mock-v1</c>).</summary>
    string Versao { get; }

    /// <summary>
    /// Calcula o score [0,100] a partir dos sinais e da config ativa. Pode lançar
    /// (ex.: indisponibilidade) — o Worker captura e aplica fail-open; a porta não
    /// esconde a falha.
    /// </summary>
    Task<int> CalcularScoreAsync(Sinistro sinistro, ScoringConfig config, CancellationToken ct = default);
}
