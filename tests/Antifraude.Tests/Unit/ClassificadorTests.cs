using Antifraude.Core.Decisao;
using Antifraude.Core.Dominio;
using FluentAssertions;
using Xunit;

namespace Antifraude.Tests.Unit;

public sealed class ClassificadorTests
{
    private static readonly ScoringConfig Config = FakeConfigRepository.ConfigPadrao; // médio=30, alto=60

    [Theory]
    [InlineData(0, Faixa.Baixo)]
    [InlineData(29, Faixa.Baixo)]
    [InlineData(30, Faixa.Medio)]
    [InlineData(59, Faixa.Medio)]
    [InlineData(60, Faixa.Alto)]
    [InlineData(100, Faixa.Alto)]
    public void FaixaPara_respeita_os_limiares_da_config(int score, Faixa esperada) =>
        Classificador.FaixaPara(score, Config).Should().Be(esperada);

    [Theory]
    [InlineData(Faixa.Baixo, Rota.Normal)]
    [InlineData(Faixa.Medio, Rota.Normal)]
    [InlineData(Faixa.Alto, Rota.Reforcada)]
    [InlineData(Faixa.Indeterminado, Rota.Reforcada)]
    public void RotaPara_sempre_uma_fila_humana(Faixa faixa, Rota esperada) =>
        Classificador.RotaPara(faixa).Should().Be(esperada);

    [Theory]
    [InlineData(-1, true)]
    [InlineData(-5, true)]
    [InlineData(101, true)]
    [InlineData(150, true)]
    [InlineData(0, false)]
    [InlineData(50, false)]
    [InlineData(100, false)]
    public void ForaDeFaixa_detecta_score_fora_de_0_100(int score, bool esperado) =>
        Classificador.ForaDeFaixa(score).Should().Be(esperado);
}
