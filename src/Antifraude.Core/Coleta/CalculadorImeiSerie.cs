using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;

namespace Antifraude.Core.Coleta;

/// <summary>
/// Sinal <c>imei_serie_divergente</c>: consulta a base de apólices e ativa o sinal tanto
/// quando o IMEI/série informado DIVERGE do cadastrado quanto quando está NÃO CADASTRADO
/// — a evidência distingue os dois motivos. Identificadores aparecem mascarados
/// (últimos 4) na evidência.
/// </summary>
public sealed class CalculadorImeiSerie(IBaseDeApolices apolices, TimeProvider? timeProvider = null)
    : ICalculadorDeSinal
{
    public const string NomeDoSinal = "imei_serie_divergente";

    private const string Origem = "base-apolices-v1";

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public string Nome => NomeDoSinal;

    public async Task<Sinal> CalcularAsync(Sinistro sinistro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sinistro);
        var agora = _clock.GetUtcNow();

        var imei = sinistro.Aparelho?.Imei;
        var serie = sinistro.Aparelho?.NumeroSerie;
        var temIdentificador = !string.IsNullOrWhiteSpace(imei) || !string.IsNullOrWhiteSpace(serie);

        if (string.IsNullOrWhiteSpace(sinistro.Apolice) || !temIdentificador)
        {
            return new Sinal(
                Nome, ValorSinal.Indisponivel, Origem,
                Evidencia: new Dictionary<string, object?>
                {
                    ["campoAusente"] = string.IsNullOrWhiteSpace(sinistro.Apolice) ? "apolice" : "imei/numeroSerie",
                },
                Motivo: MotivoIndisponibilidade.DadoAusente,
                CalculadoEm: agora);
        }

        try
        {
            var cadastrado = await apolices
                .ObterAparelhoCadastradoAsync(sinistro.Apolice!, ct)
                .ConfigureAwait(false);

            var evidencia = new Dictionary<string, object?>
            {
                ["imeiInformado"] = MascaraLgpd.Mascarar(imei),
                ["serieInformada"] = MascaraLgpd.Mascarar(serie),
                ["imeiCadastrado"] = MascaraLgpd.Mascarar(cadastrado?.Imei),
                ["serieCadastrada"] = MascaraLgpd.Mascarar(cadastrado?.NumeroSerie),
            };

            // Compara só pares em que informado E cadastrado existem.
            var imeiComparavel = !string.IsNullOrWhiteSpace(imei) && !string.IsNullOrWhiteSpace(cadastrado?.Imei);
            var serieComparavel = !string.IsNullOrWhiteSpace(serie) && !string.IsNullOrWhiteSpace(cadastrado?.NumeroSerie);

            if (cadastrado is null || (!imeiComparavel && !serieComparavel))
            {
                // Nada registrado para comparar: identificador informado NÃO consta na apólice.
                evidencia["motivo"] = "nao_cadastrado";
                return new Sinal(Nome, ValorSinal.Ativo, Origem, evidencia, CalculadoEm: agora);
            }

            var diverge = (imeiComparavel && imei != cadastrado.Imei)
                          || (serieComparavel && serie != cadastrado.NumeroSerie);
            if (diverge)
            {
                evidencia["motivo"] = "diverge";
                return new Sinal(Nome, ValorSinal.Ativo, Origem, evidencia, CalculadoEm: agora);
            }

            evidencia["motivo"] = "confere";
            return new Sinal(Nome, ValorSinal.Inativo, Origem, evidencia, CalculadoEm: agora);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new Sinal(
                Nome, ValorSinal.Indisponivel, Origem,
                Evidencia: new Dictionary<string, object?> { ["falha"] = ex.GetType().Name },
                Motivo: MotivoIndisponibilidade.FonteIndisponivel,
                CalculadoEm: agora);
        }
    }
}
