namespace Antifraude.Core.Dominio;

/// <summary>Aparelho do sinistro (Trilha B — celular). Identificadores técnicos.</summary>
/// <param name="Imei">IMEI do aparelho, quando informado.</param>
/// <param name="NumeroSerie">Número de série, quando informado.</param>
public sealed record Aparelho(string? Imei, string? NumeroSerie);

/// <summary>Metadados básicos da abertura do sinistro.</summary>
/// <param name="AbertoEm">Data/hora de abertura no sistema principal.</param>
/// <param name="Canal">Canal de abertura (ex.: app, web).</param>
/// <param name="IdCliente">Identificador do cliente.</param>
public sealed record MetadadosSinistro(DateTimeOffset? AbertoEm, string? Canal, string? IdCliente);

/// <summary>
/// Sinistro real recebido para análise (Feature 2.1). É a mensagem que trafega da API pela
/// fila até o Worker. O <see cref="IdSinistro"/> é o único campo estrutural — sua ausência
/// torna o evento não-processável. Fotos são referências (ID/URL), nunca cópias. Os
/// <see cref="Sinais"/> NÃO chegam na ingestão (são responsabilidade da coleta downstream);
/// ficam vazios até a feature 2.2 existir. O <see cref="CaseId"/> costura request → fila →
/// worker → caso → auditoria.
/// </summary>
/// <param name="CaseId">Identificador de correlação gerado na borda (API).</param>
/// <param name="IdSinistro">Identificador do sinistro no sistema principal (único estrutural).</param>
/// <param name="Apolice">Identificador da apólice, quando presente.</param>
/// <param name="Aparelho">Aparelho (IMEI/série), quando presente.</param>
/// <param name="Fotos">Referências às fotos (ID/URL), quando presentes.</param>
/// <param name="Metadados">Metadados de abertura, quando presentes.</param>
/// <param name="PayloadParcial">True quando falta algum campo não-estrutural do payload mínimo.</param>
/// <param name="Sinais">Sinais coletados. Vazio na ingestão — populado pela feature 2.2 no futuro.</param>
public sealed record Sinistro(
    Guid CaseId,
    string IdSinistro,
    string? Apolice = null,
    Aparelho? Aparelho = null,
    IReadOnlyList<string>? Fotos = null,
    MetadadosSinistro? Metadados = null,
    bool PayloadParcial = false,
    IReadOnlyList<Sinal>? Sinais = null)
{
    /// <summary>True quando não há sinais suficientes para uma decisão confiável.</summary>
    public bool SinaisIncompletos => Sinais is null || Sinais.Count == 0;
}
