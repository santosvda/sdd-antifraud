namespace Antifraude.Core.Portas;

/// <summary>Aparelho registrado na apólice (identificadores técnicos).</summary>
/// <param name="Imei">IMEI cadastrado, quando houver.</param>
/// <param name="NumeroSerie">Número de série cadastrado, quando houver.</param>
public sealed record AparelhoCadastrado(string? Imei, string? NumeroSerie);

/// <summary>
/// Porta da fonte "Base de Apólices": fornece o aparelho cadastrado para o sinal de
/// inconsistência IMEI×série. Pode lançar (indisponibilidade/timeout); o calculador
/// captura e marca o sinal como indisponível.
/// </summary>
public interface IBaseDeApolices
{
    /// <summary>
    /// Aparelho cadastrado na apólice, ou <c>null</c> quando a apólice não tem aparelho
    /// registrado ("não cadastrado" — a distinção diverge × não cadastrado é regra do
    /// calculador, não desta porta).
    /// </summary>
    Task<AparelhoCadastrado?> ObterAparelhoCadastradoAsync(string apolice, CancellationToken ct = default);
}
