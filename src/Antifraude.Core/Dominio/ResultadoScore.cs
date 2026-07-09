namespace Antifraude.Core.Dominio;

/// <summary>
/// Resultado estruturado do <see cref="Portas.IScoreProvider"/>. Não fabrica score: quando
/// não há cobertura suficiente para avaliar, <see cref="Score"/> é <c>null</c> e
/// <see cref="MotivoNaoAvaliado"/> explica a condição (o consumidor trata via fail-open).
/// </summary>
/// <param name="Score">Score em [0,100], ou <c>null</c> quando "não avaliado".</param>
/// <param name="CoberturaParcial">True quando renormalizou (exatamente 2 dos 3 sinais).</param>
/// <param name="SinaisUsados">Nomes dos sinais presentes considerados no cálculo.</param>
/// <param name="SinaisAusentes">Nomes dos sinais esperados que não vieram.</param>
/// <param name="MotivoNaoAvaliado">Causa de "não avaliado" quando <see cref="Score"/> é null.</param>
/// <param name="AtributosProibidosFiltrados">Atributos sensíveis filtrados da entrada (evento de conformidade).</param>
public sealed record ResultadoScore(
    int? Score,
    bool CoberturaParcial,
    IReadOnlyList<string> SinaisUsados,
    IReadOnlyList<string> SinaisAusentes,
    string? MotivoNaoAvaliado,
    IReadOnlyList<string> AtributosProibidosFiltrados)
{
    /// <summary>True quando um score foi efetivamente calculado.</summary>
    public bool Avaliado => Score is not null;
}
