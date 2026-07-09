using Antifraude.Core.Coleta;
using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;
using FluentAssertions;
using Xunit;

namespace Antifraude.Tests.Unit.Coleta;

public sealed class CalculadorImeiSerieTests
{
    private const string Imei = "356938035643809";
    private const string Serie = "SN-XYZ-123";

    private static Sinistro ComAparelho(string? imei = Imei, string? serie = Serie, string? apolice = "AP-1") =>
        new(Guid.NewGuid(), "SIN-1", Apolice: apolice, Aparelho: new Aparelho(imei, serie));

    [Fact]
    public async Task Imei_conferindo_e_inativo_com_identificadores_mascarados()
    {
        var fonte = new FakeBaseDeApolices();
        fonte.PorApolice["AP-1"] = new AparelhoCadastrado(Imei, Serie);

        var sinal = await new CalculadorImeiSerie(fonte).CalcularAsync(ComAparelho());

        sinal.Estado.Should().Be(ValorSinal.Inativo);
        sinal.Evidencia!["motivo"].Should().Be("confere");
        sinal.Evidencia["imeiInformado"].Should().Be("…3809", "IMEI nunca aparece in-the-clear");
    }

    [Fact]
    public async Task Imei_divergente_ativa_com_motivo_diverge()
    {
        var fonte = new FakeBaseDeApolices();
        fonte.PorApolice["AP-1"] = new AparelhoCadastrado("999999999991122", Serie);

        var sinal = await new CalculadorImeiSerie(fonte).CalcularAsync(ComAparelho(serie: null));

        sinal.Estado.Should().Be(ValorSinal.Ativo);
        sinal.Evidencia!["motivo"].Should().Be("diverge");
        sinal.Evidencia["imeiCadastrado"].Should().Be("…1122");
    }

    [Fact]
    public async Task Apolice_sem_registro_ativa_com_motivo_nao_cadastrado()
    {
        var sinal = await new CalculadorImeiSerie(new FakeBaseDeApolices()).CalcularAsync(ComAparelho());

        sinal.Estado.Should().Be(ValorSinal.Ativo);
        sinal.Evidencia!["motivo"].Should().Be("nao_cadastrado", "não cadastrado ativa o MESMO sinal que divergente, com motivo distinto");
    }

    [Fact]
    public async Task Sem_imei_e_sem_serie_e_dado_ausente_sem_tocar_a_fonte()
    {
        var fonte = new FakeBaseDeApolices();

        var sinal = await new CalculadorImeiSerie(fonte)
            .CalcularAsync(ComAparelho(imei: null, serie: null));

        sinal.Estado.Should().Be(ValorSinal.Indisponivel);
        sinal.Motivo.Should().Be(MotivoIndisponibilidade.DadoAusente);
        fonte.Chamadas.Should().Be(0);
    }

    [Fact]
    public async Task Sem_apolice_e_dado_ausente()
    {
        var sinal = await new CalculadorImeiSerie(new FakeBaseDeApolices())
            .CalcularAsync(ComAparelho(apolice: null));

        sinal.Estado.Should().Be(ValorSinal.Indisponivel);
        sinal.Motivo.Should().Be(MotivoIndisponibilidade.DadoAusente);
    }

    [Fact]
    public async Task Fonte_fora_e_indisponivel_por_fonte_externa()
    {
        var fonte = new FakeBaseDeApolices { Lancar = true };

        var sinal = await new CalculadorImeiSerie(fonte).CalcularAsync(ComAparelho());

        sinal.Estado.Should().Be(ValorSinal.Indisponivel);
        sinal.Motivo.Should().Be(MotivoIndisponibilidade.FonteIndisponivel);
    }
}
