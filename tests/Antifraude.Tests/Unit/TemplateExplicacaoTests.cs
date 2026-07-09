using Antifraude.Core.Classificacao;
using Antifraude.Core.Dominio;
using FluentAssertions;
using Xunit;

namespace Antifraude.Tests.Unit;

public sealed class TemplateExplicacaoTests
{
    [Theory]
    [InlineData(MotivoSemClassificacao.SinalAusente)]
    [InlineData(MotivoSemClassificacao.ProviderIndisponivel)]
    [InlineData(MotivoSemClassificacao.ConfigIndisponivel)]
    [InlineData(MotivoSemClassificacao.ConfigCorrompida)]
    [InlineData(MotivoSemClassificacao.ScoreForaDeFaixa)]
    public void Rotulo_canonico_e_nao_acusatorio_e_nao_inventa_faixa(MotivoSemClassificacao motivo)
    {
        var rotulo = TemplateExplicacao.RotuloCanonico(motivo);

        rotulo.Should().NotBeNullOrWhiteSpace();
        rotulo.Should().NotContainAny("fraude", "culpado", "mentiu", "confirmado");
        // Sem classificação não inventa faixa.
        rotulo.Should().NotContainAny("faixa baixa", "faixa média", "faixa alta");
    }

    [Fact]
    public void Rotulo_canonico_e_deterministico()
    {
        TemplateExplicacao.RotuloCanonico(MotivoSemClassificacao.ScoreForaDeFaixa)
            .Should().Be(TemplateExplicacao.RotuloCanonico(MotivoSemClassificacao.ScoreForaDeFaixa));
    }

    [Fact]
    public void Faixa_indeterminada_nao_tem_texto_de_faixa()
    {
        var act = () => TemplateExplicacao.FaixaTexto(Faixa.Indeterminado);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
