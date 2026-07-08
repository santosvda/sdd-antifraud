namespace Antifraude.Infra.Persistencia;

/// <summary>
/// Linha de <c>imagem_hashes</c>: pHash de uma foto de um sinistro, para o sinal de reuso
/// de imagem dos próximos casos (janela móvel de 6 meses aplicada na consulta). Único por
/// (sinistro, foto) — o registro é upsert idempotente.
/// </summary>
public sealed class ImagemHashRegistro
{
    public long Id { get; init; }

    public string IdSinistro { get; init; } = string.Empty;

    public string FotoRef { get; init; } = string.Empty;

    /// <summary>pHash de 64 bits armazenado como BIGINT (cast bit a bit de ulong).</summary>
    public long Phash { get; init; }

    public DateTimeOffset CriadoEm { get; init; }
}

/// <summary>
/// Linha de <c>apolices</c>: aparelho (IMEI/série) registrado por apólice. Fonte fake
/// local desta fatia — semeada pelo <see cref="DbSeeder"/> para a demo.
/// </summary>
public sealed class ApoliceRegistro
{
    public string Apolice { get; init; } = string.Empty;

    public string? Imei { get; init; }

    public string? NumeroSerie { get; init; }
}

/// <summary>
/// Linha de <c>historico_sinistros</c>: sinistros já vistos, por cliente/IMEI, para o
/// sinal de velocity. Alimentada pela própria coleta (upsert idempotente por sinistro).
/// </summary>
public sealed class HistoricoSinistroRegistro
{
    public string IdSinistro { get; init; } = string.Empty;

    public string? IdCliente { get; init; }

    public string? Imei { get; init; }

    public DateTimeOffset AbertoEm { get; init; }
}
