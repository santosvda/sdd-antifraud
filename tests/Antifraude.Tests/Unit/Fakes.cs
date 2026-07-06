using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;

namespace Antifraude.Tests.Unit;

/// <summary>Repo de config em memória para os testes unitários do motor.</summary>
internal sealed class FakeConfigRepository(ScoringConfig? config = null) : IScoringConfigRepository
{
    private readonly ScoringConfig? _config = config;

    public static ScoringConfig ConfigPadrao => new()
    {
        Versao = 3,
        Ativa = true,
        Pesos = new Dictionary<string, double>
        {
            ["reuso_imagem"] = 30,
            ["imei_serie_divergente"] = 25,
            ["geo"] = 20,
        },
        LimiarMedio = 30,
        LimiarAlto = 60,
        CriadaEm = DateTimeOffset.UnixEpoch,
    };

    public Task<ScoringConfig> ObterAtivaAsync(CancellationToken ct = default) =>
        _config is null
            ? throw new InvalidOperationException("sem config ativa")
            : Task.FromResult(_config);
}

/// <summary>Provider de score controlável: valor fixo ou queda sob demanda.</summary>
internal sealed class FakeScoreProvider(int score = 0, bool lancar = false, string versao = "fake-v1")
    : IScoreProvider
{
    public string Versao => versao;

    public Task<int> CalcularScoreAsync(Sinistro sinistro, ScoringConfig config, CancellationToken ct = default) =>
        lancar
            ? throw new InvalidOperationException("provider indisponível (simulado)")
            : Task.FromResult(score);
}
