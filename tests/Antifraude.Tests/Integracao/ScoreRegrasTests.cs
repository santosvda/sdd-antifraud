using Antifraude.Core.Decisao;
using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Antifraude.Tests.Integracao;

/// <summary>
/// Feature 2.3 ponta-a-ponta com o banco real: motor de regras determinístico + config v2
/// resolvida do MySQL + persistência do caso/auditoria. Como a API ainda não ingere sinais
/// (feature 2.2), os sinais são injetados direto no <see cref="MotorDeDecisao"/>.
/// </summary>
[Collection(IntegrationCollection.Nome)]
public sealed class ScoreRegrasTests(IntegrationFixture fixture)
{
    private static Sinal S(string nome, double valor = 1.0) =>
        new(nome, valor != 0 ? ValorSinal.Ativo : ValorSinal.Inativo, "teste");

    private static Sinistro ComSinais(params Sinal[] sinais) =>
        new(Guid.NewGuid(), $"SIN-{Guid.NewGuid():N}", Sinais: sinais);

    private static async Task<Caso> AvaliarEPersistirAsync(IServiceProvider sp, Sinistro sinistro)
    {
        using var scope = sp.CreateScope();
        var s = scope.ServiceProvider;
        var motor = s.GetRequiredService<MotorDeDecisao>();
        var casos = s.GetRequiredService<ICaseRepository>();
        var auditoria = s.GetRequiredService<IAuditLog>();

        var r = await motor.AvaliarAsync(sinistro);
        await casos.SalvarAsync(r.Caso);
        await auditoria.RegistrarAsync(r.Auditoria);
        return (await casos.ObterPorIdAsync(sinistro.CaseId))!;
    }

    [Fact]
    public async Task Tres_sinais_verdadeiros_pontuam_alto_com_config_v2()
    {
        await using var factory = new AntifraudeApiFactory(fixture);

        var caso = await AvaliarEPersistirAsync(
            factory.Services,
            ComSinais(S(SinaisConhecidos.ReusoImagem), S(SinaisConhecidos.ImeiSerie), S(SinaisConhecidos.Velocity)));

        caso.Estado.Should().Be(EstadoDoCaso.RoteadoParaRevisao);
        caso.Score.Should().Be(100);
        caso.Faixa.Should().Be(Faixa.Alto);
        caso.CoberturaParcial.Should().BeFalse();
        caso.VersaoConfig.Should().Be(2, "a v2 é a config ativa semeada");
        caso.VersaoProvider.Should().Be("regras-v1");
    }

    [Fact]
    public async Task Dois_sinais_renormalizam_e_marcam_cobertura_parcial()
    {
        await using var factory = new AntifraudeApiFactory(fixture);

        var caso = await AvaliarEPersistirAsync(
            factory.Services,
            ComSinais(S(SinaisConhecidos.ReusoImagem), S(SinaisConhecidos.ImeiSerie)));

        caso.Estado.Should().Be(EstadoDoCaso.RoteadoParaRevisao);
        caso.Score.Should().Be(100, "50 e 30 renormalizados para somar 100");
        caso.CoberturaParcial.Should().BeTrue();
    }

    [Fact]
    public async Task Um_unico_sinal_nao_e_avaliado_e_vira_pendente_revisao_manual()
    {
        await using var factory = new AntifraudeApiFactory(fixture);

        var caso = await AvaliarEPersistirAsync(
            factory.Services,
            ComSinais(S(SinaisConhecidos.ReusoImagem)));

        caso.Estado.Should().Be(EstadoDoCaso.PendenteRevisaoManual);
        caso.Score.Should().BeNull("cobertura abaixo do piso de 2 sinais");
        caso.CoberturaParcial.Should().BeFalse();
    }
}
