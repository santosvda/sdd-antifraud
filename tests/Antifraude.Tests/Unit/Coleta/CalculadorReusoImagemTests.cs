using Antifraude.Core.Coleta;
using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;
using FluentAssertions;
using Xunit;

namespace Antifraude.Tests.Unit.Coleta;

public sealed class CalculadorReusoImagemTests
{
    private static Sinistro ComFotos(params string[] fotos) =>
        new(Guid.NewGuid(), "SIN-ATUAL", Fotos: fotos);

    [Fact]
    public async Task Colisao_dentro_do_limiar_ativa_com_evidencia_da_melhor_colisao()
    {
        var fonte = new FakeRepositorioDeImagens();
        fonte.HashPorFoto["foto-1"] = 0b1111UL; // 4 bits ligados
        fonte.Historico.Add(new HashHistorico("SIN-LONGE", 0b1111UL << 20)); // distância 8
        fonte.Historico.Add(new HashHistorico("SIN-IGUAL", 0b1111UL));       // distância 0 (melhor)

        var sinal = await new CalculadorReusoImagem(fonte).CalcularAsync(ComFotos("foto-1"));

        sinal.Estado.Should().Be(ValorSinal.Ativo);
        sinal.Origem.Should().Be("phash-fake-teste");
        var colisoes = sinal.Evidencia!["colisoes"].Should()
            .BeAssignableTo<IReadOnlyList<Dictionary<string, object?>>>().Subject;
        colisoes.Should().ContainSingle();
        colisoes[0]["sinistroColidido"].Should().Be("SIN-IGUAL");
        colisoes[0]["distancia"].Should().Be(0);
    }

    [Fact]
    public async Task Sem_colisao_e_inativo_com_evidencia_dos_hashes_comparados()
    {
        var fonte = new FakeRepositorioDeImagens();
        fonte.HashPorFoto["foto-1"] = 0UL;
        fonte.Historico.Add(new HashHistorico("SIN-OUTRO", ulong.MaxValue)); // distância 64

        var sinal = await new CalculadorReusoImagem(fonte).CalcularAsync(ComFotos("foto-1"));

        sinal.Estado.Should().Be(ValorSinal.Inativo);
        sinal.Evidencia!["hashesComparados"].Should().Be(1);
    }

    [Fact]
    public async Task Distancia_11_fica_fora_do_limiar()
    {
        var fonte = new FakeRepositorioDeImagens();
        fonte.HashPorFoto["foto-1"] = 0UL;
        fonte.Historico.Add(new HashHistorico("SIN-QUASE", (1UL << 11) - 1)); // 11 bits diferentes

        var sinal = await new CalculadorReusoImagem(fonte).CalcularAsync(ComFotos("foto-1"));

        sinal.Estado.Should().Be(ValorSinal.Inativo);
    }

    [Fact]
    public async Task Sem_foto_e_dado_ausente_sem_tocar_a_fonte()
    {
        var fonte = new FakeRepositorioDeImagens();

        var sinal = await new CalculadorReusoImagem(fonte)
            .CalcularAsync(new Sinistro(Guid.NewGuid(), "SIN-SEM-FOTO"));

        sinal.Estado.Should().Be(ValorSinal.Indisponivel);
        sinal.Motivo.Should().Be(MotivoIndisponibilidade.DadoAusente);
        fonte.Chamadas.Should().Be(0, "dado ausente não consulta a fonte externa");
    }

    [Fact]
    public async Task Fonte_fora_e_indisponivel_por_fonte_externa()
    {
        var fonte = new FakeRepositorioDeImagens { Lancar = true };

        var sinal = await new CalculadorReusoImagem(fonte).CalcularAsync(ComFotos("foto-1"));

        sinal.Estado.Should().Be(ValorSinal.Indisponivel);
        sinal.Motivo.Should().Be(MotivoIndisponibilidade.FonteIndisponivel);
    }

    [Fact]
    public async Task Registra_hashes_do_caso_atual_apos_o_calculo()
    {
        var fonte = new FakeRepositorioDeImagens();
        fonte.HashPorFoto["foto-1"] = 42UL;

        await new CalculadorReusoImagem(fonte).CalcularAsync(ComFotos("foto-1"));

        fonte.Registrados.Should().ContainSingle(r => r.IdSinistro == "SIN-ATUAL");
    }

    [Theory]
    [InlineData(0UL, 0UL, 0)]
    [InlineData(0UL, 1UL, 1)]
    [InlineData(0UL, ulong.MaxValue, 64)]
    [InlineData(0b1010UL, 0b0101UL, 4)]
    public void Distancia_de_hamming_conta_bits_diferentes(ulong a, ulong b, int esperado) =>
        CalculadorReusoImagem.DistanciaHamming(a, b).Should().Be(esperado);
}
