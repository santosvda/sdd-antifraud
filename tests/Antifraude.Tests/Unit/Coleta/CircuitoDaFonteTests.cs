using Antifraude.Infra.Fontes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Antifraude.Tests.Unit.Coleta;

public sealed class CircuitoDaFonteTests
{
    private static CircuitoDaFonte Circuito(FonteResilienteOptions? options = null) =>
        new("teste", options ?? new FonteResilienteOptions(), NullLogger.Instance);

    [Fact]
    public async Task Sucesso_devolve_o_resultado_da_acao()
    {
        var resultado = await Circuito().ExecutarAsync("op", _ => Task.FromResult(42), CancellationToken.None);

        resultado.Should().Be(42);
    }

    [Fact]
    public async Task Timeout_da_fonte_vira_TimeoutException_que_o_calculador_converte_em_indisponivel()
    {
        var circuito = Circuito(new FonteResilienteOptions { Timeout = TimeSpan.FromMilliseconds(50) });

        var acao = async () => await circuito.ExecutarAsync<int>(
            "op-lenta",
            async token =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), token);
                return 0;
            },
            CancellationToken.None);

        await acao.Should().ThrowAsync<TimeoutException>("estouro de timeout não pode virar espera infinita");
    }

    [Fact]
    public async Task Falhas_consecutivas_abrem_o_circuito_e_a_proxima_chamada_falha_imediato()
    {
        var circuito = Circuito(new FonteResilienteOptions { FalhasParaAbrir = 3 });
        var chamadasNaFonte = 0;

        Task<int> FonteQuebrada(CancellationToken _)
        {
            chamadasNaFonte++;
            throw new InvalidOperationException("fonte fora");
        }

        for (var i = 0; i < 3; i++)
        {
            await ((Func<Task>)(() => circuito.ExecutarAsync("op", FonteQuebrada, CancellationToken.None)))
                .Should().ThrowAsync<InvalidOperationException>();
        }

        chamadasNaFonte.Should().Be(3);

        // Circuito aberto: a 4ª chamada falha SEM tocar a fonte (sem esperar timeout).
        var comCircuitoAberto = () => circuito.ExecutarAsync("op", FonteQuebrada, CancellationToken.None);
        await comCircuitoAberto.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Circuito da fonte 'teste' aberto*");
        chamadasNaFonte.Should().Be(3, "com o circuito aberto a fonte não é consultada");
    }

    [Fact]
    public async Task Sucesso_zera_o_contador_de_falhas()
    {
        var circuito = Circuito(new FonteResilienteOptions { FalhasParaAbrir = 2 });

        // 1 falha, depois sucesso, depois mais 1 falha: o circuito NÃO abre (não são consecutivas).
        await ((Func<Task>)(() => circuito.ExecutarAsync<int>("op", _ => throw new InvalidOperationException("x"), CancellationToken.None)))
            .Should().ThrowAsync<InvalidOperationException>();
        (await circuito.ExecutarAsync("op", _ => Task.FromResult(1), CancellationToken.None)).Should().Be(1);
        await ((Func<Task>)(() => circuito.ExecutarAsync<int>("op", _ => throw new InvalidOperationException("x"), CancellationToken.None)))
            .Should().ThrowAsync<InvalidOperationException>();

        // Segue fechado: a próxima chamada ainda alcança a fonte.
        (await circuito.ExecutarAsync("op", _ => Task.FromResult(2), CancellationToken.None)).Should().Be(2);
    }

    [Fact]
    public async Task Simular_indisponibilidade_falha_toda_chamada_imediatamente()
    {
        var circuito = Circuito(new FonteResilienteOptions { SimularIndisponibilidade = true });
        var chamadas = 0;

        var acao = () => circuito.ExecutarAsync("op", _ => Task.FromResult(++chamadas), CancellationToken.None);

        await acao.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*simular indisponibilidade*");
        chamadas.Should().Be(0);
    }
}
