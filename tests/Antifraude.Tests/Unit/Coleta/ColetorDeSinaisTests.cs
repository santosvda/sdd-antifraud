using Antifraude.Core.Coleta;
using Antifraude.Core.Dominio;
using FluentAssertions;
using Xunit;

namespace Antifraude.Tests.Unit.Coleta;

public sealed class ColetorDeSinaisTests
{
    private sealed class CalculadorFixo(string nome, ValorSinal estado) : ICalculadorDeSinal
    {
        public string Nome => nome;

        public Task<Sinal> CalcularAsync(Sinistro sinistro, CancellationToken ct = default) =>
            Task.FromResult(new Sinal(nome, estado, "teste"));
    }

    private sealed class CalculadorQueLanca(string nome) : ICalculadorDeSinal
    {
        public string Nome => nome;

        public Task<Sinal> CalcularAsync(Sinistro sinistro, CancellationToken ct = default) =>
            throw new InvalidOperationException("bug interno do calculador");
    }

    private static readonly Sinistro Sinistro = new(Guid.NewGuid(), "SIN-1");

    [Fact]
    public async Task Produz_um_sinal_por_calculador()
    {
        var coletor = new ColetorDeSinais([
            new CalculadorFixo("a", ValorSinal.Ativo),
            new CalculadorFixo("b", ValorSinal.Inativo),
            new CalculadorFixo("c", ValorSinal.Indisponivel),
        ]);

        var sinais = await coletor.ColetarAsync(Sinistro);

        sinais.Should().HaveCount(3);
        sinais.Select(s => s.Nome).Should().BeEquivalentTo("a", "b", "c");
    }

    [Fact]
    public async Task Excecao_de_um_calculador_nao_escapa_nem_afeta_os_demais()
    {
        var coletor = new ColetorDeSinais([
            new CalculadorFixo("ok", ValorSinal.Ativo),
            new CalculadorQueLanca("quebrado"),
            new CalculadorFixo("outro-ok", ValorSinal.Inativo),
        ]);

        var sinais = await coletor.ColetarAsync(Sinistro);

        sinais.Should().HaveCount(3, "a saída sempre contém os 3 sinais");
        sinais.Single(s => s.Nome == "ok").Estado.Should().Be(ValorSinal.Ativo);
        sinais.Single(s => s.Nome == "outro-ok").Estado.Should().Be(ValorSinal.Inativo);

        var quebrado = sinais.Single(s => s.Nome == "quebrado");
        quebrado.Estado.Should().Be(ValorSinal.Indisponivel);
        quebrado.Motivo.Should().Be(MotivoIndisponibilidade.FonteIndisponivel);
    }
}
