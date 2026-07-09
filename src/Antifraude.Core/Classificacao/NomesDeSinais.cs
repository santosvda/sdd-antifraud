namespace Antifraude.Core.Classificacao;

/// <summary>
/// Mapa (em código, versionado junto do template) do identificador técnico do sinal para o
/// seu nome de exibição em linguagem de indício. Sinal desconhecido recebe um fallback seguro
/// — o identificador técnico cru NUNCA vaza para o texto exibido ao analista.
/// </summary>
public static class NomesDeSinais
{
    private const string Fallback = "outro indicador";

    private static readonly IReadOnlyDictionary<string, string> Mapa = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["reuso_imagem"] = "reuso de imagem",
        ["imei_serie_divergente"] = "inconsistência de IMEI×série",
        ["geolocalizacao_inconsistente"] = "geolocalização inconsistente",
        ["alta_frequencia_sinistros"] = "alta frequência de sinistros",
    };

    /// <summary>Nome de exibição do sinal, ou um fallback seguro se o id não for conhecido.</summary>
    public static string Exibicao(string idTecnico) =>
        Mapa.TryGetValue(idTecnico, out var nome) ? nome : Fallback;
}
