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
    /// Avalia o sinistro a partir dos sinais e da config ativa, devolvendo um
    /// <see cref="ResultadoScore"/> estruturado (score, cobertura parcial, sinais usados/ausentes,
    /// motivo de "não avaliado", atributos proibidos filtrados). Pode lançar (ex.:
    /// indisponibilidade) — o Worker captura e aplica fail-open; a porta não esconde a falha.
    /// Nunca fabrica score: cobertura insuficiente devolve <see cref="ResultadoScore.Score"/> null.
    /// </summary>
    Task<ResultadoScore> CalcularScoreAsync(Sinistro sinistro, ScoringConfig config, CancellationToken ct = default);
}
