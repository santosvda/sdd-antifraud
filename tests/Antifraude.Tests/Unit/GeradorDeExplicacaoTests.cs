using Antifraude.Core.Classificacao;
using Antifraude.Core.Dominio;
using FluentAssertions;
using Xunit;

namespace Antifraude.Tests.Unit;

public sealed class GeradorDeExplicacaoTests
{
    private static Sinal S(string nome, double valor) => new(nome, valor, "teste");

    [Fact]
    public void Nomeia_sinais_ativados_em_linguagem_de_indicio()
    {
        var texto = GeradorDeExplicacao.Gerar(
            72, Faixa.Alto, [S("reuso_imagem", 1.0), S("imei_serie_divergente", 1.0)], coberturaParcial: false);

        texto.Should().Contain("72/100");
        texto.Should().Contain("faixa alta");
        texto.Should().Contain("indícios de reuso de imagem e inconsistência de IMEI×série");
    }

    [Fact]
    public void Nunca_afirma_fraude_como_fato_consumado()
    {
        var texto = GeradorDeExplicacao.Gerar(
            90, Faixa.Alto, [S("reuso_imagem", 1.0)], coberturaParcial: false);

        texto.Should().NotContainAny("fraude confirmada", "confirmado", "culpado", "mentiu");
    }

    [Fact]
    public void Sinal_desconhecido_usa_fallback_sem_vazar_id_tecnico()
    {
        var texto = GeradorDeExplicacao.Gerar(
            50, Faixa.Medio, [S("sinal_secreto_xyz", 1.0)], coberturaParcial: false);

        texto.Should().Contain("outro indicador");
        texto.Should().NotContain("sinal_secreto_xyz");
    }

    [Fact]
    public void So_nomeia_sinais_com_valor_positivo()
    {
        var texto = GeradorDeExplicacao.Gerar(
            40, Faixa.Medio, [S("reuso_imagem", 0.0), S("imei_serie_divergente", 1.0)], coberturaParcial: false);

        texto.Should().Contain("inconsistência de IMEI×série");
        texto.Should().NotContain("reuso de imagem");
    }

    [Fact]
    public void Menciona_cobertura_parcial_quando_true()
    {
        var texto = GeradorDeExplicacao.Gerar(
            55, Faixa.Medio, [S("reuso_imagem", 1.0)], coberturaParcial: true);

        texto.Should().Contain("cobertura parcial");
    }

    [Fact]
    public void Nao_menciona_cobertura_parcial_quando_false()
    {
        var texto = GeradorDeExplicacao.Gerar(
            55, Faixa.Medio, [S("reuso_imagem", 1.0)], coberturaParcial: false);

        texto.Should().NotContain("cobertura parcial");
    }

    [Fact]
    public void E_deterministico_para_a_mesma_entrada()
    {
        var sinais = new[] { S("reuso_imagem", 1.0), S("imei_serie_divergente", 0.7) };

        var a = GeradorDeExplicacao.Gerar(72, Faixa.Alto, sinais, coberturaParcial: true);
        var b = GeradorDeExplicacao.Gerar(72, Faixa.Alto, sinais, coberturaParcial: true);

        a.Should().Be(b);
    }
}
