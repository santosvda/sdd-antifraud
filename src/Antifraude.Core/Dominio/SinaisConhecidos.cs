namespace Antifraude.Core.Dominio;

/// <summary>
/// Conjunto fechado dos 3 sinais que o motor de regras (Feature 2.3) conhece e pontua.
/// Nomes canônicos compartilhados com a coleta de sinais (Feature 2.2): qualquer nome
/// fora deste conjunto não influencia o score (whitelist — nega por padrão).
/// </summary>
public static class SinaisConhecidos
{
    public const string ReusoImagem = "reuso_imagem";

    public const string ImeiSerie = "imei_serie";

    public const string Velocity = "velocity";

    /// <summary>Conjunto esperado, na ordem de peso-base da v2 (50/30/20).</summary>
    public static readonly IReadOnlyList<string> Esperados = [ReusoImagem, ImeiSerie, Velocity];
}
