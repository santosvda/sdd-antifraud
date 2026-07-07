using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;

namespace Antifraude.Infra.Score;

/// <summary>
/// Implementação placeholder e <b>sinalizada</b> de <see cref="IScoreProvider"/>. Na
/// fundação não existe o motor determinístico real (fatia 1) nem ML (roadmap) — este mock
/// calcula um score a partir dos pesos da config só para o walking skeleton, e carimba
/// <see cref="Versao"/> = <c>mock-v1</c> para que a auditoria registre que o score veio de
/// um mock. Nenhum número é fabricado sem essa sinalização.
///
/// Expõe um modo "simular indisponibilidade" que lança sob demanda, provando o fail-open.
/// </summary>
public sealed class MockScoreProvider(MockScoreProviderOptions options) : IScoreProvider
{
    public string Versao => "mock-v1";

    public Task<int> CalcularScoreAsync(Sinistro sinistro, ScoringConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sinistro);
        ArgumentNullException.ThrowIfNull(config);

        if (options.SimularIndisponibilidade)
        {
            throw new InvalidOperationException("Provider mock em modo 'simular indisponibilidade'.");
        }

        // Soma ponderada placeholder: peso da config × intensidade do sinal.
        double bruto = 0;
        foreach (var sinal in sinistro.Sinais ?? [])
        {
            if (config.Pesos.TryGetValue(sinal.Nome, out var peso))
            {
                bruto += peso * sinal.Valor;
            }
        }

        var score = (int)Math.Round(Math.Clamp(bruto, 0, 100));
        return Task.FromResult(score);
    }
}
