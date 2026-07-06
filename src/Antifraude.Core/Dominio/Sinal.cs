namespace Antifraude.Core.Dominio;

/// <summary>
/// Um sinal coletado sobre o sinistro (ex.: reuso de imagem, divergência IMEI×série).
/// <see cref="Origem"/> é sempre carimbada — na fundação os sinais chegam mockados e
/// sinalizados; nenhum valor é fabricado silenciosamente.
/// </summary>
/// <param name="Nome">Identificador do sinal (ex.: <c>reuso_imagem</c>).</param>
/// <param name="Valor">Intensidade/presença do sinal, normalizada em [0,1].</param>
/// <param name="Origem">Proveniência do sinal (ex.: <c>mock</c>, <c>pHash</c>).</param>
public sealed record Sinal(string Nome, double Valor, string Origem);
