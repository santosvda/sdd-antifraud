using Antifraude.Core.Dominio;

namespace Antifraude.Api.Contratos;

/// <summary>Aparelho no corpo do <c>POST /sinistros</c> (IMEI/série).</summary>
public sealed record AparelhoRequest(string? Imei, string? NumeroSerie);

/// <summary>Metadados de abertura no corpo do <c>POST /sinistros</c>.</summary>
public sealed record MetadadosRequest(DateTimeOffset? AbertoEm, string? Canal, string? IdCliente);

/// <summary>
/// Corpo do <c>POST /sinistros</c> — payload de sinistro real (Feature 2.1). O único campo
/// estrutural é <see cref="IdSinistro"/>; sua ausência torna o evento não-processável. Os
/// demais campos são opcionais — sua ausência marca o caso como <c>payloadParcial</c>. Os
/// sinais de fraude NÃO entram na ingestão (são calculados downstream). A API não decide
/// mérito: só valida o formato mínimo e roteia.
/// </summary>
public sealed record SinistroRequest(
    string? IdSinistro,
    string? Apolice,
    AparelhoRequest? Aparelho,
    IReadOnlyList<string>? Fotos,
    MetadadosRequest? Metadados)
{
    /// <summary>Único campo estrutural — sua presença é o que torna o evento processável.</summary>
    public bool TemIdSinistro => !string.IsNullOrWhiteSpace(IdSinistro);

    public bool TemApolice => !string.IsNullOrWhiteSpace(Apolice);

    public bool TemAparelho =>
        Aparelho is not null && (!string.IsNullOrWhiteSpace(Aparelho.Imei) || !string.IsNullOrWhiteSpace(Aparelho.NumeroSerie));

    public bool TemFotos => Fotos is { Count: > 0 };

    public bool TemMetadados =>
        Metadados is not null &&
        (Metadados.AbertoEm is not null || !string.IsNullOrWhiteSpace(Metadados.Canal) || !string.IsNullOrWhiteSpace(Metadados.IdCliente));

    /// <summary>Parcial quando falta qualquer campo não-estrutural do payload mínimo.</summary>
    public bool PayloadParcial => !(TemApolice && TemAparelho && TemFotos && TemMetadados);

    /// <summary>Converte para o modelo de domínio com o <paramref name="caseId"/> de correlação.</summary>
    public Sinistro ParaDominio(Guid caseId) => new(
        caseId,
        IdSinistro!,
        TemApolice ? Apolice : null,
        Aparelho is null ? null : new Aparelho(Aparelho.Imei, Aparelho.NumeroSerie),
        Fotos,
        Metadados is null ? null : new MetadadosSinistro(Metadados.AbertoEm, Metadados.Canal, Metadados.IdCliente),
        PayloadParcial,
        Sinais: null);
}
