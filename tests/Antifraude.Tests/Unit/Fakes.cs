using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;

namespace Antifraude.Tests.Unit;

/// <summary>Alerta técnico que apenas registra o que foi emitido, para asserção nos testes.</summary>
internal sealed class FakeAlertaTecnico : IAlertaTecnico
{
    private readonly List<(SeveridadeAlerta Severidade, string Codigo, Guid CaseId)> _emitidos = [];

    public IReadOnlyList<(SeveridadeAlerta Severidade, string Codigo, Guid CaseId)> Emitidos => _emitidos;

    public Task EmitirAsync(
        SeveridadeAlerta severidade, string codigo, Guid caseId, string? detalhe = null, CancellationToken ct = default)
    {
        _emitidos.Add((severidade, codigo, caseId));
        return Task.CompletedTask;
    }
}

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

/// <summary>Provider de score controlável: valor fixo, resultado explícito, ou queda sob demanda.</summary>
internal sealed class FakeScoreProvider(int score = 0, bool lancar = false, string versao = "fake-v1", ResultadoScore? resultado = null)
    : IScoreProvider
{
    public string Versao => versao;

    public Task<ResultadoScore> CalcularScoreAsync(Sinistro sinistro, ScoringConfig config, CancellationToken ct = default) =>
        lancar
            ? throw new InvalidOperationException("provider indisponível (simulado)")
            : Task.FromResult(resultado ?? new ResultadoScore(score, false, [], [], null, []));
}
