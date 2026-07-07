namespace Antifraude.Infra.Persistencia;

/// <summary>
/// Entrada do store de deduplicação (idempotência). Persistência interna da Infra — o Core
/// só conhece a porta <c>ISinistroDedupStore</c>. Retida por 24h (TTL lógico + purga).
/// </summary>
public sealed class SinistroProcessado
{
    public string IdSinistro { get; init; } = string.Empty;

    public DateTimeOffset PrimeiraVezEm { get; set; }
}
