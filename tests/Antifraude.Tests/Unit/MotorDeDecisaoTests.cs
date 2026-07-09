using Antifraude.Core.Decisao;
using Antifraude.Core.Dominio;
using FluentAssertions;
using Xunit;

namespace Antifraude.Tests.Unit;

public sealed class MotorDeDecisaoTests
{
    private static Sinistro ComSinais() =>
        new(Guid.NewGuid(), "SIN-1", Sinais: [new Sinal("reuso_imagem", ValorSinal.Ativo, "mock")]);

    private static MotorDeDecisao Motor(int score = 0, bool provedorCai = false, ScoringConfig? config = null) =>
        new(new FakeConfigRepository(config ?? FakeConfigRepository.ConfigPadrao),
            new FakeScoreProvider(score, provedorCai));

    [Fact]
    public async Task Score_alto_roteia_para_reforcada_sem_bloquear()
    {
        var r = await Motor(score: 80).AvaliarAsync(ComSinais());

        r.Caso.Estado.Should().Be(EstadoDoCaso.RoteadoParaRevisao);
        r.Caso.Faixa.Should().Be(Faixa.Alto);
        r.Caso.Rota.Should().Be(Rota.Reforcada);
        r.Caso.Score.Should().Be(80);
    }

    [Fact]
    public async Task Score_baixo_roteia_para_normal()
    {
        var r = await Motor(score: 10).AvaliarAsync(ComSinais());

        r.Caso.Faixa.Should().Be(Faixa.Baixo);
        r.Caso.Rota.Should().Be(Rota.Normal);
        r.Caso.Estado.Should().Be(EstadoDoCaso.RoteadoParaRevisao);
    }

    [Fact]
    public async Task Carimba_versao_da_config_e_do_provider()
    {
        var r = await Motor(score: 50).AvaliarAsync(ComSinais());

        r.Caso.VersaoConfig.Should().Be(3);
        r.Caso.VersaoProvider.Should().Be("fake-v1");
        r.Auditoria.VersaoConfig.Should().Be(3);
        r.Auditoria.VersaoProvider.Should().Be("fake-v1");
    }

    [Fact]
    public async Task Auditoria_correlaciona_pelo_mesmo_caseId()
    {
        var sinistro = ComSinais();
        var r = await Motor(score: 50).AvaliarAsync(sinistro);

        r.Caso.CaseId.Should().Be(sinistro.CaseId);
        r.Auditoria.CaseId.Should().Be(sinistro.CaseId);
    }

    [Fact]
    public async Task Provider_indisponivel_gera_fail_open_sem_lancar()
    {
        var r = await Motor(provedorCai: true).AvaliarAsync(ComSinais());

        r.Caso.Estado.Should().Be(EstadoDoCaso.PendenteRevisaoManual);
        r.Caso.Faixa.Should().Be(Faixa.Indeterminado);
        r.Caso.Rota.Should().Be(Rota.Reforcada);
        r.Caso.Score.Should().BeNull();
        r.Auditoria.Causa.Should().Contain("indisponível");
        r.Auditoria.Score.Should().BeNull();
    }

    [Fact]
    public async Task Provider_nao_avaliado_gera_fail_open_com_dados_incompletos()
    {
        var naoAvaliado = new ResultadoScore(
            Score: null, CoberturaParcial: false, SinaisUsados: [], SinaisAusentes: ["velocity"],
            MotivoNaoAvaliado: "Cobertura abaixo do piso: 1 de 3 sinais presentes.", AtributosProibidosFiltrados: []);
        var motor = new MotorDeDecisao(new FakeConfigRepository(FakeConfigRepository.ConfigPadrao), new FakeScoreProvider(resultado: naoAvaliado));

        var r = await motor.AvaliarAsync(ComSinais());

        r.Caso.Estado.Should().Be(EstadoDoCaso.PendenteRevisaoManual);
        r.Caso.DadosIncompletos.Should().BeTrue();
        r.Caso.Score.Should().BeNull("não se assume score baixo nem alto por omissão");
        r.Auditoria.Causa.Should().Contain("Cobertura abaixo do piso");
    }

    [Fact]
    public async Task Cobertura_parcial_pontua_e_marca_caso_e_auditoria()
    {
        var parcial = new ResultadoScore(
            Score: 100, CoberturaParcial: true, SinaisUsados: ["reuso_imagem", "imei_serie_divergente"],
            SinaisAusentes: ["velocity"], MotivoNaoAvaliado: null, AtributosProibidosFiltrados: []);
        var motor = new MotorDeDecisao(new FakeConfigRepository(FakeConfigRepository.ConfigPadrao), new FakeScoreProvider(resultado: parcial));

        var r = await motor.AvaliarAsync(ComSinais());

        r.Caso.Estado.Should().Be(EstadoDoCaso.RoteadoParaRevisao);
        r.Caso.Score.Should().Be(100);
        r.Caso.CoberturaParcial.Should().BeTrue();
        r.Auditoria.CoberturaParcial.Should().BeTrue();
    }

    [Fact]
    public async Task Atributo_proibido_filtrado_vira_evento_de_conformidade_na_auditoria()
    {
        var comProibido = new ResultadoScore(
            Score: 50, CoberturaParcial: false, SinaisUsados: ["reuso_imagem", "imei_serie_divergente", "velocity"],
            SinaisAusentes: [], MotivoNaoAvaliado: null, AtributosProibidosFiltrados: ["idade"]);
        var motor = new MotorDeDecisao(new FakeConfigRepository(FakeConfigRepository.ConfigPadrao), new FakeScoreProvider(resultado: comProibido));

        var r = await motor.AvaliarAsync(ComSinais());

        r.Caso.Estado.Should().Be(EstadoDoCaso.RoteadoParaRevisao);
        r.Auditoria.Causa.Should().Contain("Atributos proibidos filtrados").And.Contain("idade");
    }

    [Fact]
    public async Task Indisponibilidade_parcial_segue_para_score_com_dados_incompletos()
    {
        var sinistro = new Sinistro(Guid.NewGuid(), "SIN-3", Sinais:
        [
            new Sinal("reuso_imagem", ValorSinal.Ativo, "mock"),
            new Sinal("velocity", ValorSinal.Indisponivel, "mock", Motivo: MotivoIndisponibilidade.FonteIndisponivel),
        ]);

        var r = await Motor(score: 40).AvaliarAsync(sinistro);

        r.Caso.Estado.Should().Be(EstadoDoCaso.RoteadoParaRevisao, "1 sinal indisponível não é fail-open");
        r.Caso.Score.Should().Be(40, "o score usa os sinais disponíveis");
        r.Caso.DadosIncompletos.Should().BeTrue("a indisponibilidade fica visível ao analista");
    }

    [Fact]
    public async Task Todos_os_sinais_indisponiveis_e_fail_open_sem_score()
    {
        var sinistro = new Sinistro(Guid.NewGuid(), "SIN-4", Sinais:
        [
            new Sinal("reuso_imagem", ValorSinal.Indisponivel, "mock", Motivo: MotivoIndisponibilidade.FonteIndisponivel),
            new Sinal("imei_serie_divergente", ValorSinal.Indisponivel, "mock", Motivo: MotivoIndisponibilidade.DadoAusente),
            new Sinal("velocity", ValorSinal.Indisponivel, "mock", Motivo: MotivoIndisponibilidade.FonteIndisponivel),
        ]);

        var r = await Motor(score: 90).AvaliarAsync(sinistro);

        r.Caso.Estado.Should().Be(EstadoDoCaso.PendenteRevisaoManual, "3/3 indisponíveis equivale a não avaliado");
        r.Caso.Score.Should().BeNull();
        r.Caso.DadosIncompletos.Should().BeTrue();
        r.Auditoria.Sinais.Should().HaveCount(3, "os motivos de cada sinal ficam auditados");
    }

    [Fact]
    public async Task Sem_config_ativa_tambem_e_fail_open()
    {
        var motor = new MotorDeDecisao(new FakeConfigRepository(config: null), new FakeScoreProvider(50));

        var r = await motor.AvaliarAsync(ComSinais());

        r.Caso.Estado.Should().Be(EstadoDoCaso.PendenteRevisaoManual);
        r.Caso.VersaoConfig.Should().Be(0);
        r.Auditoria.Causa.Should().Contain("scoring_config");
    }

    [Fact]
    public async Task Nunca_produz_estado_que_bloqueia_ou_nega()
    {
        // Varre uma gama de scores e o modo de queda: em nenhum ramo há veredito.
        foreach (var score in new[] { 0, 30, 60, 100 })
        {
            var r = await Motor(score).AvaliarAsync(ComSinais());
            r.Caso.Rota.Should().BeOneOf(Rota.Normal, Rota.Reforcada);
            r.Caso.Estado.Should().BeOneOf(EstadoDoCaso.RoteadoParaRevisao, EstadoDoCaso.PendenteRevisaoManual);
        }
    }

    [Fact]
    public async Task Caso_classificado_gera_explicacao_e_carimba_versao_template()
    {
        var r = await Motor(score: 80).AvaliarAsync(ComSinais());

        r.Caso.Explicacao.Should().NotBeNullOrWhiteSpace();
        r.Caso.Explicacao.Should().Contain("reuso de imagem");
        r.Caso.VersaoTemplate.Should().Be("tmpl-v1");
        r.Caso.Motivo.Should().BeNull();
        r.Auditoria.Explicacao.Should().Be(r.Caso.Explicacao);
        r.Auditoria.VersaoTemplate.Should().Be("tmpl-v1");
    }

    [Fact]
    public async Task Score_fora_de_faixa_vira_anomalia_com_alerta_severidade_alta()
    {
        var alerta = new FakeAlertaTecnico();
        var motor = new MotorDeDecisao(
            new FakeConfigRepository(FakeConfigRepository.ConfigPadrao),
            new FakeScoreProvider(score: -5),
            alertaTecnico: alerta);
        var sinistro = ComSinais();

        var r = await motor.AvaliarAsync(sinistro);

        r.Caso.Estado.Should().Be(EstadoDoCaso.PendenteRevisaoManual);
        r.Caso.Faixa.Should().Be(Faixa.Indeterminado);
        r.Caso.Score.Should().BeNull("o score fora de faixa não é coagido para dentro do intervalo");
        r.Caso.Motivo.Should().Be(MotivoSemClassificacao.ScoreForaDeFaixa);
        r.Caso.Explicacao.Should().BeNull();

        alerta.Emitidos.Should().ContainSingle();
        alerta.Emitidos[0].Severidade.Should().Be(SeveridadeAlerta.Alta);
        alerta.Emitidos[0].Codigo.Should().Be(nameof(MotivoSemClassificacao.ScoreForaDeFaixa));
        alerta.Emitidos[0].CaseId.Should().Be(sinistro.CaseId);
    }

    [Fact]
    public async Task Provider_indisponivel_carimba_motivo_e_nao_emite_alerta()
    {
        var alerta = new FakeAlertaTecnico();
        var motor = new MotorDeDecisao(
            new FakeConfigRepository(FakeConfigRepository.ConfigPadrao),
            new FakeScoreProvider(lancar: true),
            alertaTecnico: alerta);

        var r = await motor.AvaliarAsync(ComSinais());

        r.Caso.Motivo.Should().Be(MotivoSemClassificacao.ProviderIndisponivel);
        alerta.Emitidos.Should().BeEmpty("indisponibilidade esperada não é anomalia técnica");
    }

    [Fact]
    public async Task Sinais_faltantes_carimbam_motivo_sinal_ausente()
    {
        var sinistro = new Sinistro(Guid.NewGuid(), "SIN-3", Sinais: []);

        var r = await Motor(score: 50).AvaliarAsync(sinistro);

        r.Caso.Motivo.Should().Be(MotivoSemClassificacao.SinalAusente);
    }
}
