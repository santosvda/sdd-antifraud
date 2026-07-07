namespace Antifraude.Core.Portas;

/// <summary>
/// Store de deduplicação por <c>idSinistro</c> (idempotência). Retém o id por uma janela
/// (TTL de 24h); dentro dela, o mesmo sinistro nunca é reprocessado. A implementação vive na
/// borda (Infra); o fail-open quando o store está indisponível é responsabilidade de quem
/// chama (nunca bloqueia o enfileiramento por causa da checagem).
/// </summary>
public interface ISinistroDedupStore
{
    /// <summary>
    /// Registra o <paramref name="idSinistro"/> se ele for novo dentro da janela de 24h.
    /// Retorna <c>true</c> quando é a primeira vez (registrado agora) e <c>false</c> quando
    /// já foi visto dentro da janela (duplicado).
    /// </summary>
    Task<bool> RegistrarSeNovoAsync(string idSinistro, CancellationToken ct = default);

    /// <summary>Remove entradas expiradas (mais antigas que a janela de 24h). Retorna a quantidade removida.</summary>
    Task<int> PurgarExpiradosAsync(CancellationToken ct = default);
}
