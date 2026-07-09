using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;

namespace Antifraude.Core.Coleta;

/// <summary>
/// Sinal <c>velocity</c>: ativo quando há ≥2 sinistros do mesmo cliente OU mesmo
/// aparelho (IMEI) na janela de 90 dias, contada a partir de <c>abertoEm</c> (fallback:
/// data de processamento — a evidência registra qual referência foi usada). A contagem
/// nunca inclui o sinistro atual (a porta exclui pelo id); após o cálculo o sinistro é
/// registrado no histórico (upsert idempotente).
/// </summary>
public sealed class CalculadorVelocity(IHistoricoDeSinistros historico, TimeProvider? timeProvider = null)
    : ICalculadorDeSinal
{
    public const string NomeDoSinal = "velocity";

    private const string Origem = "historico-sinistros-v1";
    private const int JanelaDias = 90;
    private const int Limiar = 2;

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public string Nome => NomeDoSinal;

    public async Task<Sinal> CalcularAsync(Sinistro sinistro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sinistro);
        var agora = _clock.GetUtcNow();

        var idCliente = sinistro.Metadados?.IdCliente;
        var imei = sinistro.Aparelho?.Imei;
        if (string.IsNullOrWhiteSpace(idCliente) && string.IsNullOrWhiteSpace(imei))
        {
            return new Sinal(
                Nome, ValorSinal.Indisponivel, Origem,
                Evidencia: new Dictionary<string, object?> { ["campoAusente"] = "idCliente/imei" },
                Motivo: MotivoIndisponibilidade.DadoAusente,
                CalculadoEm: agora);
        }

        var abertoEm = sinistro.Metadados?.AbertoEm;
        var referencia = abertoEm ?? agora;

        try
        {
            var contagem = await historico
                .ContarAsync(sinistro.IdSinistro, idCliente, imei, referencia.AddDays(-JanelaDias), ct)
                .ConfigureAwait(false);

            // Registro APÓS o cálculo (upsert idempotente): o caso não se conta.
            await historico.RegistrarAsync(sinistro.IdSinistro, idCliente, imei, referencia, ct)
                .ConfigureAwait(false);

            var ativo = contagem.PorCliente >= Limiar || contagem.PorAparelho >= Limiar;
            var evidencia = new Dictionary<string, object?>
            {
                ["contagemCliente"] = contagem.PorCliente,
                ["contagemAparelho"] = contagem.PorAparelho,
                ["limiar"] = Limiar,
                ["janelaDias"] = JanelaDias,
                ["referenciaTemporal"] = abertoEm.HasValue ? "abertoEm" : "processamento",
            };

            return new Sinal(
                Nome,
                ativo ? ValorSinal.Ativo : ValorSinal.Inativo,
                Origem,
                evidencia,
                CalculadoEm: agora);
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
