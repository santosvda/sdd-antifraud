namespace Antifraude.Infra.Score;

/// <summary>Opções do provider mock, ajustáveis por env var na borda.</summary>
public sealed class MockScoreProviderOptions
{
    /// <summary>
    /// Quando true, o provider lança ao ser chamado — alimenta o teste de não-bloqueio
    /// (fail-open) e a demonstração de que o sinistro segue mesmo com o provider caído.
    /// </summary>
    public bool SimularIndisponibilidade { get; set; }
}
