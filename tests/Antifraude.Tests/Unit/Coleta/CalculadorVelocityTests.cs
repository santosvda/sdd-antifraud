using Antifraude.Core.Coleta;
using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;
using FluentAssertions;
using Xunit;

namespace Antifraude.Tests.Unit.Coleta;

public sealed class CalculadorVelocityTests
{
    private static readonly DateTimeOffset AbertoEm = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static Sinistro ComCliente(string? idCliente = "CLI-1", string? imei = "356938035643809", DateTimeOffset? abertoEm = null) =>
        new(Guid.NewGuid(), "SIN-1",
            Aparelho: imei is null ? null : new Aparelho(imei, null),
            Metadados: new MetadadosSinistro(abertoEm ?? AbertoEm, "app", idCliente));

    [Fact]
    public async Task Dois_anteriores_do_mesmo_cliente_ativa_com_contagem_e_janela()
    {
        var fonte = new FakeHistoricoDeSinistros { Contagem = new ContagemHistorico(2, 0) };

        var sinal = await new CalculadorVelocity(fonte).CalcularAsync(ComCliente());

        sinal.Estado.Should().Be(ValorSinal.Ativo);
        sinal.Evidencia!["contagemCliente"].Should().Be(2);
        sinal.Evidencia["janelaDias"].Should().Be(90);
        sinal.Evidencia["referenciaTemporal"].Should().Be("abertoEm");
    }

    [Fact]
    public async Task Dois_anteriores_do_mesmo_aparelho_tambem_ativa()
    {
        var fonte = new FakeHistoricoDeSinistros { Contagem = new ContagemHistorico(0, 3) };

        var sinal = await new CalculadorVelocity(fonte).CalcularAsync(ComCliente());

        sinal.Estado.Should().Be(ValorSinal.Ativo);
        sinal.Evidencia!["contagemAparelho"].Should().Be(3);
    }

    [Fact]
    public async Task Menos_de_dois_e_inativo()
    {
        var fonte = new FakeHistoricoDeSinistros { Contagem = new ContagemHistorico(1, 1) };

        var sinal = await new CalculadorVelocity(fonte).CalcularAsync(ComCliente());

        sinal.Estado.Should().Be(ValorSinal.Inativo);
    }

    [Fact]
    public async Task Janela_de_90_dias_conta_a_partir_do_abertoEm()
    {
        var fonte = new FakeHistoricoDeSinistros();

        await new CalculadorVelocity(fonte).CalcularAsync(ComCliente());

        fonte.DesdeRecebido.Should().Be(AbertoEm.AddDays(-90));
    }

    [Fact]
    public async Task Sem_cliente_e_sem_imei_e_dado_ausente_sem_tocar_a_fonte()
    {
        var fonte = new FakeHistoricoDeSinistros();

        var sinal = await new CalculadorVelocity(fonte)
            .CalcularAsync(ComCliente(idCliente: null, imei: null));

        sinal.Estado.Should().Be(ValorSinal.Indisponivel);
        sinal.Motivo.Should().Be(MotivoIndisponibilidade.DadoAusente);
        fonte.Chamadas.Should().Be(0);
    }

    [Fact]
    public async Task So_o_imei_presente_ja_permite_calcular()
    {
        var fonte = new FakeHistoricoDeSinistros { Contagem = new ContagemHistorico(0, 2) };

        var sinal = await new CalculadorVelocity(fonte).CalcularAsync(ComCliente(idCliente: null));

        sinal.Estado.Should().Be(ValorSinal.Ativo);
    }

    [Fact]
    public async Task Fonte_fora_e_indisponivel_por_fonte_externa()
    {
        var fonte = new FakeHistoricoDeSinistros { Lancar = true };

        var sinal = await new CalculadorVelocity(fonte).CalcularAsync(ComCliente());

        sinal.Estado.Should().Be(ValorSinal.Indisponivel);
        sinal.Motivo.Should().Be(MotivoIndisponibilidade.FonteIndisponivel);
    }

    [Fact]
    public async Task Registra_o_sinistro_atual_no_historico_apos_o_calculo()
    {
        var fonte = new FakeHistoricoDeSinistros();

        await new CalculadorVelocity(fonte).CalcularAsync(ComCliente());

        fonte.Registrados.Should().ContainSingle(r => r.IdSinistro == "SIN-1" && r.IdCliente == "CLI-1");
    }
}
