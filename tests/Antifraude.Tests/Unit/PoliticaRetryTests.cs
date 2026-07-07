using Antifraude.Infra.Mensageria;
using FluentAssertions;
using Xunit;

namespace Antifraude.Tests.Unit;

public sealed class PoliticaRetryTests
{
    // Backoffs minúsculos: exercita a lógica sem esperar segundos reais.
    private static readonly TimeSpan[] Curto = [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)];

    [Fact]
    public async Task Falha_transitoria_e_superada_por_retry()
    {
        var tentativas = 0;

        await PoliticaRetry.ExecutarComBackoffAsync(
            _ =>
            {
                tentativas++;
                return tentativas < 3
                    ? throw new InvalidOperationException("falha transitória")
                    : Task.CompletedTask;
            },
            Curto,
            TimeProvider.System);

        tentativas.Should().Be(3, "falhou 2x e teve sucesso na 3ª tentativa");
    }

    [Fact]
    public async Task Falha_persistente_lanca_enfileiramento_apos_esgotar()
    {
        var tentativas = 0;

        var acao = async () => await PoliticaRetry.ExecutarComBackoffAsync(
            _ =>
            {
                tentativas++;
                throw new InvalidOperationException("fila fora");
            },
            [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)],
            TimeProvider.System);

        await acao.Should().ThrowAsync<EnfileiramentoException>();
        tentativas.Should().Be(2, "esgotou as 2 tentativas configuradas");
    }
}
