using Antifraude.Core.Decisao;
using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;
using Antifraude.Infra.Persistencia;
using Antifraude.Tests.Unit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Antifraude.Tests.Integracao;

[Collection(IntegrationCollection.Nome)]
public sealed class ClassificacaoRiscoTests(IntegrationFixture fixture)
{
    private static Sinistro ComSinais(params Sinal[] sinais) =>
        new(Guid.NewGuid(), $"SIN-{Guid.NewGuid():N}", Sinais: sinais);

    /// <summary>
    /// Sobe a API real (migrations + seed) com o <see cref="IScoreProvider"/> trocado por um
    /// test-double (injeta score, inclusive fora de faixa) e um espião de alerta técnico. Roda o
    /// motor DI-wired sobre o MySQL real e devolve o caso persistido + os alertas emitidos.
    /// </summary>
    private async Task<(Caso Caso, FakeAlertaTecnico Alertas)> ProcessarAsync(int score, bool provedorCai, Sinistro sinistro)
    {
        var spy = new FakeAlertaTecnico();
        await using var factory = new AntifraudeApiFactory(fixture, configurarServicos: services =>
        {
            services.AddSingleton<IScoreProvider>(new FakeScoreProvider(score, provedorCai));
            services.AddSingleton<IAlertaTecnico>(spy);
        });
        _ = factory.CreateClient(); // força o start do host (migrations + seed da scoring_config)

        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var motor = sp.GetRequiredService<MotorDeDecisao>();
        var casos = sp.GetRequiredService<ICaseRepository>();
        var auditoria = sp.GetRequiredService<IAuditLog>();

        var r = await motor.AvaliarAsync(sinistro);
        await casos.SalvarAsync(r.Caso);
        await auditoria.RegistrarAsync(r.Auditoria);

        var persistido = await casos.ObterPorIdAsync(sinistro.CaseId);
        return (persistido!, spy);
    }

    private AntifraudeDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<AntifraudeDbContext>()
            .UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString))
            .Options;
        return new AntifraudeDbContext(options);
    }

    [Fact]
    public async Task Classifica_com_sinais_e_persiste_explicacao_e_versoes_no_caso_e_na_auditoria()
    {
        var sinistro = ComSinais(new Sinal("reuso_imagem", 1.0, "teste"), new Sinal("imei_serie_divergente", 1.0, "teste"));

        var (caso, alertas) = await ProcessarAsync(score: 72, provedorCai: false, sinistro);

        caso.Estado.Should().Be(EstadoDoCaso.RoteadoParaRevisao);
        caso.Faixa.Should().Be(Faixa.Alto); // limiar alto semeado = 60
        caso.Explicacao.Should().NotBeNullOrWhiteSpace();
        caso.Explicacao.Should().Contain("reuso de imagem");
        caso.VersaoTemplate.Should().Be("tmpl-v1");
        alertas.Emitidos.Should().BeEmpty("caso classificado não é anomalia");

        // A trilha imutável carimba a explicação e a versão de template.
        await using var db = NovoContexto();
        var registro = await db.Auditoria.AsNoTracking().FirstAsync(a => a.CaseId == sinistro.CaseId);
        registro.Explicacao.Should().Be(caso.Explicacao);
        registro.VersaoTemplate.Should().Be("tmpl-v1");
    }

    [Fact]
    public async Task Score_fora_de_faixa_nao_classifica_e_emite_alerta_severidade_alta()
    {
        var sinistro = ComSinais(new Sinal("reuso_imagem", 1.0, "teste"));

        var (caso, alertas) = await ProcessarAsync(score: -5, provedorCai: false, sinistro);

        caso.Estado.Should().Be(EstadoDoCaso.PendenteRevisaoManual);
        caso.Faixa.Should().Be(Faixa.Indeterminado);
        caso.Score.Should().BeNull("o valor fora de faixa não é coagido");
        caso.Motivo.Should().Be(MotivoSemClassificacao.ScoreForaDeFaixa);
        caso.Explicacao.Should().BeNull();

        alertas.Emitidos.Should().ContainSingle();
        alertas.Emitidos[0].Severidade.Should().Be(SeveridadeAlerta.Alta);
        alertas.Emitidos[0].Codigo.Should().Be(nameof(MotivoSemClassificacao.ScoreForaDeFaixa));
    }

    [Fact]
    public async Task Fail_open_esperado_carimba_motivo_e_nao_emite_alerta_tecnico()
    {
        var sinistro = ComSinais(new Sinal("reuso_imagem", 1.0, "teste"));

        var (caso, alertas) = await ProcessarAsync(score: 0, provedorCai: true, sinistro);

        caso.Estado.Should().Be(EstadoDoCaso.PendenteRevisaoManual);
        caso.Motivo.Should().Be(MotivoSemClassificacao.ProviderIndisponivel);
        alertas.Emitidos.Should().BeEmpty("indisponibilidade esperada não dispara alerta técnico");
    }
}
