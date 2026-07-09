using Antifraude.Core.Decisao;
using Antifraude.Core.Dominio;
using FluentAssertions;
using Xunit;

namespace Antifraude.Tests.Unit;

public sealed class MotorDeRegrasTests
{
    private static readonly ScoringConfig ConfigV2 = new()
    {
        Versao = 2,
        Ativa = true,
        Pesos = new Dictionary<string, double>
        {
            [SinaisConhecidos.ReusoImagem] = 50,
            [SinaisConhecidos.ImeiSerie] = 30,
            [SinaisConhecidos.Velocity] = 20,
        },
        LimiarMedio = 30,
        LimiarAlto = 71,
        CriadaEm = DateTimeOffset.UnixEpoch,
    };

    private static readonly MotorDeRegras Motor = new();

    private static Sinistro Com(params Sinal[] sinais) =>
        new(Guid.NewGuid(), "SIN", Sinais: sinais);

    private static Sinal S(string nome, double valor = 1.0) =>
        new(nome, valor != 0 ? ValorSinal.Ativo : ValorSinal.Inativo, "teste");

    [Fact]
    public async Task Tres_sinais_verdadeiros_somam_os_pesos()
    {
        var r = await Motor.CalcularScoreAsync(
            Com(S(SinaisConhecidos.ReusoImagem), S(SinaisConhecidos.ImeiSerie), S(SinaisConhecidos.Velocity)), ConfigV2);

        r.Score.Should().Be(100);
        r.CoberturaParcial.Should().BeFalse();
    }

    [Fact]
    public async Task Sinal_presente_e_falso_soma_zero_sem_cobertura_parcial()
    {
        var r = await Motor.CalcularScoreAsync(
            Com(S(SinaisConhecidos.ReusoImagem), S(SinaisConhecidos.ImeiSerie), S(SinaisConhecidos.Velocity, 0)), ConfigV2);

        r.Score.Should().Be(80, "os 3 sinais estão presentes; velocity presente e falso soma 0");
        r.CoberturaParcial.Should().BeFalse();
    }

    [Fact]
    public async Task Nome_de_sinal_desconhecido_nao_entra_no_calculo()
    {
        var r = await Motor.CalcularScoreAsync(
            Com(S(SinaisConhecidos.ReusoImagem), S(SinaisConhecidos.ImeiSerie), S(SinaisConhecidos.Velocity), S("xpto")), ConfigV2);

        r.Score.Should().Be(100, "o sinal desconhecido é descartado");
        r.CoberturaParcial.Should().BeFalse();
    }

    [Fact]
    public async Task Dois_sinais_presentes_renormalizam_e_marcam_cobertura_parcial()
    {
        var r = await Motor.CalcularScoreAsync(
            Com(S(SinaisConhecidos.ReusoImagem), S(SinaisConhecidos.ImeiSerie)), ConfigV2);

        r.CoberturaParcial.Should().BeTrue();
        r.Score.Should().Be(100, "50 e 30 renormalizados para somar 100, ambos verdadeiros");
        r.SinaisAusentes.Should().Contain(SinaisConhecidos.Velocity);
    }

    [Fact]
    public async Task Renormalizacao_com_um_verdadeiro_arredonda_deterministicamente()
    {
        // reuso presente+true (50), imei presente+false (30), velocity ausente.
        // Presentes = {reuso, imei} → renorm fator 100/80 = 1.25; só reuso é verdadeiro → 50*1.25 = 62.5 → 63.
        var r = await Motor.CalcularScoreAsync(
            Com(S(SinaisConhecidos.ReusoImagem), S(SinaisConhecidos.ImeiSerie, 0)), ConfigV2);

        r.CoberturaParcial.Should().BeTrue();
        r.Score.Should().Be(63, "62,5 arredondado com MidpointRounding.AwayFromZero");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Cobertura_abaixo_do_piso_nao_avalia(int quantos)
    {
        var sinais = quantos == 0 ? [] : new[] { S(SinaisConhecidos.ReusoImagem) };

        var r = await Motor.CalcularScoreAsync(Com(sinais), ConfigV2);

        r.Score.Should().BeNull();
        r.Avaliado.Should().BeFalse();
        r.MotivoNaoAvaliado.Should().Contain("piso");
    }

    [Fact]
    public async Task Calculo_e_deterministico()
    {
        var sinistro = Com(S(SinaisConhecidos.ReusoImagem), S(SinaisConhecidos.Velocity));

        var r1 = await Motor.CalcularScoreAsync(sinistro, ConfigV2);
        var r2 = await Motor.CalcularScoreAsync(sinistro, ConfigV2);

        r1.Score.Should().Be(r2.Score);
        r1.CoberturaParcial.Should().Be(r2.CoberturaParcial);
    }

    [Fact]
    public async Task Atributo_proibido_e_filtrado_e_reportado()
    {
        var r = await Motor.CalcularScoreAsync(
            Com(S(SinaisConhecidos.ReusoImagem), S(SinaisConhecidos.ImeiSerie), S(SinaisConhecidos.Velocity), S("idade")), ConfigV2);

        r.Score.Should().Be(100, "o atributo proibido não influencia o score");
        r.AtributosProibidosFiltrados.Should().Contain("idade");
    }
}
