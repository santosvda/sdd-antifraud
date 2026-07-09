using System.Numerics;
using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;

namespace Antifraude.Core.Coleta;

/// <summary>
/// Sinal <c>reuso_imagem</c>: compara o pHash (64 bits) de cada foto do sinistro atual
/// com o histórico dos últimos 6 meses; reuso confirmado com distância de Hamming ≤ 10.
/// A evidência traz, por foto colidida, a melhor colisão (menor distância). Após o
/// cálculo, registra os hashes do sinistro atual para os próximos casos (upsert — a
/// consulta ao histórico já exclui o próprio sinistro na porta).
/// </summary>
public sealed class CalculadorReusoImagem(IRepositorioDeImagens imagens, TimeProvider? timeProvider = null)
    : ICalculadorDeSinal
{
    public const string NomeDoSinal = "reuso_imagem";

    private const int LimiarHamming = 10;
    private const int JanelaMeses = 6;

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public string Nome => NomeDoSinal;

    public async Task<Sinal> CalcularAsync(Sinistro sinistro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sinistro);
        var agora = _clock.GetUtcNow();

        if (sinistro.Fotos is not { Count: > 0 })
        {
            return new Sinal(
                Nome, ValorSinal.Indisponivel, imagens.Origem,
                Evidencia: new Dictionary<string, object?> { ["campoAusente"] = "fotos" },
                Motivo: MotivoIndisponibilidade.DadoAusente,
                CalculadoEm: agora);
        }

        try
        {
            var hashes = await imagens.ObterHashesAsync(sinistro.Fotos, ct).ConfigureAwait(false);
            var historico = await imagens
                .ObterHistoricoAsync(sinistro.IdSinistro, agora.AddMonths(-JanelaMeses), ct)
                .ConfigureAwait(false);

            var colisoes = new List<Dictionary<string, object?>>();
            foreach (var foto in hashes)
            {
                HashHistorico? melhor = null;
                var melhorDistancia = int.MaxValue;
                foreach (var anterior in historico)
                {
                    var distancia = DistanciaHamming(foto.Phash, anterior.Phash);
                    if (distancia <= LimiarHamming && distancia < melhorDistancia)
                    {
                        melhor = anterior;
                        melhorDistancia = distancia;
                    }
                }

                if (melhor is not null)
                {
                    colisoes.Add(new Dictionary<string, object?>
                    {
                        ["foto"] = foto.FotoRef,
                        ["sinistroColidido"] = melhor.IdSinistro,
                        ["distancia"] = melhorDistancia,
                    });
                }
            }

            // Registro APÓS o cálculo (upsert idempotente): o caso não colide consigo mesmo.
            await imagens.RegistrarHashesAsync(sinistro.IdSinistro, hashes, agora, ct).ConfigureAwait(false);

            var evidencia = colisoes.Count > 0
                ? new Dictionary<string, object?> { ["colisoes"] = colisoes, ["janelaMeses"] = JanelaMeses }
                : new Dictionary<string, object?> { ["hashesComparados"] = historico.Count, ["janelaMeses"] = JanelaMeses };

            return new Sinal(
                Nome,
                colisoes.Count > 0 ? ValorSinal.Ativo : ValorSinal.Inativo,
                imagens.Origem,
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
                Nome, ValorSinal.Indisponivel, imagens.Origem,
                Evidencia: new Dictionary<string, object?> { ["falha"] = ex.GetType().Name },
                Motivo: MotivoIndisponibilidade.FonteIndisponivel,
                CalculadoEm: agora);
        }
    }

    /// <summary>Bits diferentes entre dois pHashes de 64 bits.</summary>
    public static int DistanciaHamming(ulong a, ulong b) => BitOperations.PopCount(a ^ b);
}
