using System.Text.Json.Serialization;

namespace Antifraude.Core.Dominio;

/// <summary>
/// Estado de um sinal coletado. <see cref="Indisponivel"/> é distinto de
/// <see cref="Inativo"/>: indisponível significa "não foi possível calcular" e nunca é
/// convertido em falso por conveniência — é essa distinção que permite à fase de score
/// renormalizar os pesos corretamente.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ValorSinal
{
    Ativo,
    Inativo,
    Indisponivel,
}

/// <summary>Por que um sinal ficou <see cref="ValorSinal.Indisponivel"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MotivoIndisponibilidade
{
    /// <summary>O dado de entrada necessário está ausente no payload (ex.: sem foto).</summary>
    DadoAusente,

    /// <summary>A fonte externa necessária está inacessível (timeout/circuito aberto).</summary>
    FonteIndisponivel,
}

/// <summary>
/// Um sinal coletado sobre o sinistro (ex.: reuso de imagem, divergência IMEI×série).
/// Tri-estado: ativo / inativo / indisponível — nunca um booleano que esconda a
/// impossibilidade de cálculo. <see cref="Origem"/> é sempre carimbada (ex.:
/// <c>phash-fake-v1</c>); nenhum valor é fabricado silenciosamente. A
/// <see cref="Evidencia"/> é um objeto pequeno e estruturado com o que motivou o valor
/// (identificadores sensíveis sempre mascarados) — vai imutável para a auditoria.
/// </summary>
/// <param name="Nome">Identificador do sinal (ex.: <c>reuso_imagem</c>).</param>
/// <param name="Estado">Tri-estado do sinal.</param>
/// <param name="Origem">Proveniência do cálculo (ex.: <c>phash-fake-v1</c>).</param>
/// <param name="Evidencia">O que motivou o valor (ex.: sinistro colidido e distância).</param>
/// <param name="Motivo">Motivo da indisponibilidade, quando <see cref="ValorSinal.Indisponivel"/>.</param>
/// <param name="CalculadoEm">Timestamp do cálculo.</param>
public sealed record Sinal(
    string Nome,
    ValorSinal Estado,
    string Origem,
    IReadOnlyDictionary<string, object?>? Evidencia = null,
    MotivoIndisponibilidade? Motivo = null,
    DateTimeOffset CalculadoEm = default)
{
    [JsonIgnore]
    public bool Indisponivel => Estado == ValorSinal.Indisponivel;
}
