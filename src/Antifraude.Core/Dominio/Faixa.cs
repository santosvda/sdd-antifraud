namespace Antifraude.Core.Dominio;

/// <summary>Faixa de risco resultante do score, por limiar configurável na <c>scoring_config</c>.</summary>
public enum Faixa
{
    /// <summary>Score não pôde ser determinado (fail-open). Sempre roteia para revisão manual.</summary>
    Indeterminado,
    Baixo,
    Medio,
    Alto,
}
