using System.Net;
using System.Text.Json;
using Antifraude.Core.Coleta;
using Antifraude.Core.Dominio;
using Antifraude.Infra.Persistencia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Antifraude.Tests.Integracao;

/// <summary>
/// Cenários de integração da coleta de sinais (feature 2.2), espelhando o Gherkin do PRD:
/// sinais calculados com evidência, indisponibilidade por fonte/dado, reuso e velocity
/// ponta a ponta — sempre via API → fila → worker (simulado) → MySQL.
/// </summary>
[Collection(IntegrationCollection.Nome)]
public sealed class ColetaSinaisTests(IntegrationFixture fixture)
{
    /// <summary>IMEI/série que CONFEREM com a apólice semeada AP-2026-0042.</summary>
    private const string ApoliceSeed = "AP-2026-0042";
    private const string ImeiSeed = "356938035643809";
    private const string SerieSeed = "SN-XYZ-123";

    private static string NovoId() => $"SIN-{Guid.NewGuid():N}";

    private static object Payload(
        string idSinistro,
        string? apolice = ApoliceSeed,
        object? aparelho = null,
        string[]? fotos = null,
        string? idCliente = null) => new
        {
            idSinistro,
            apolice,
            aparelho = aparelho ?? new { imei = ImeiSeed, numeroSerie = SerieSeed },
            fotos = fotos ?? [$"img://repo/{idSinistro}"],
            metadados = new
            {
                abertoEm = DateTimeOffset.UtcNow,
                canal = "app",
                idCliente = idCliente ?? $"CLI-{idSinistro}",
            },
        };

    private AntifraudeDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<AntifraudeDbContext>()
            .UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString))
            .Options;
        return new AntifraudeDbContext(options);
    }

    private async Task<RegistroAuditoria> AuditoriaDoCasoAsync(Guid caseId)
    {
        await using var db = NovoContexto();
        return await db.Auditoria.AsNoTracking().SingleAsync(a => a.CaseId == caseId);
    }

    private static async Task<Caso> PostarEProcessarAsync(AntifraudeApiFactory factory, object payload)
    {
        var client = factory.CreateClient();
        var (status, caseId) = await WorkerSimulado.PostarAsync(client, payload);
        status.Should().Be(HttpStatusCode.Accepted);

        var caso = await WorkerSimulado.ProcessarUmaAsync(factory.Services, caseId);
        caso.Should().NotBeNull();
        return caso!;
    }

    private static Sinal SinalDe(RegistroAuditoria auditoria, string nome) =>
        auditoria.Sinais.Should().ContainSingle(s => s.Nome == nome).Subject;

    [Fact]
    public async Task Fluxo_feliz_calcula_os_3_sinais_com_evidencia_na_auditoria()
    {
        await using var factory = new AntifraudeApiFactory(fixture);

        var caso = await PostarEProcessarAsync(factory, Payload(NovoId()));

        caso.Estado.Should().Be(EstadoDoCaso.RoteadoParaRevisao);
        caso.DadosIncompletos.Should().BeFalse("nenhum sinal ficou indisponível");

        var auditoria = await AuditoriaDoCasoAsync(caso.CaseId);
        auditoria.Sinais.Should().HaveCount(3);
        auditoria.Sinais.Should().OnlyContain(s => s.Estado != ValorSinal.Indisponivel);
        auditoria.Sinais.Should().OnlyContain(s => s.Evidencia != null, "toda decisão de sinal carrega evidência");

        SinalDe(auditoria, CalculadorImeiSerie.NomeDoSinal).Estado
            .Should().Be(ValorSinal.Inativo, "o IMEI/série confere com a apólice semeada");
    }

    [Fact]
    public async Task Fonte_de_imagens_fora_marca_so_o_reuso_como_indisponivel()
    {
        await using var factory = new AntifraudeApiFactory(fixture, settings: new Dictionary<string, string>
        {
            ["FONTE_IMAGENS_INDISPONIVEL"] = "true",
        });

        var caso = await PostarEProcessarAsync(factory, Payload(NovoId()));

        // Indisponibilidade parcial: o caso segue com score e a marca de dados incompletos.
        caso.Estado.Should().Be(EstadoDoCaso.RoteadoParaRevisao);
        caso.Score.Should().NotBeNull("os outros 2 sinais foram calculados");
        caso.DadosIncompletos.Should().BeTrue();

        var auditoria = await AuditoriaDoCasoAsync(caso.CaseId);
        var reuso = SinalDe(auditoria, CalculadorReusoImagem.NomeDoSinal);
        reuso.Estado.Should().Be(ValorSinal.Indisponivel);
        reuso.Motivo.Should().Be(MotivoIndisponibilidade.FonteIndisponivel);

        SinalDe(auditoria, CalculadorImeiSerie.NomeDoSinal).Estado.Should().NotBe(ValorSinal.Indisponivel);
        SinalDe(auditoria, CalculadorVelocity.NomeDoSinal).Estado.Should().NotBe(ValorSinal.Indisponivel);
    }

    [Fact]
    public async Task Payload_parcial_sem_aparelho_marca_imei_serie_como_dado_ausente()
    {
        await using var factory = new AntifraudeApiFactory(fixture);

        var id = NovoId();
        var caso = await PostarEProcessarAsync(factory, new
        {
            idSinistro = id,
            apolice = ApoliceSeed,
            fotos = new[] { $"img://repo/{id}" },
            metadados = new { abertoEm = DateTimeOffset.UtcNow, canal = "app", idCliente = $"CLI-{id}" },
        });

        caso.PayloadParcial.Should().BeTrue("faltou o aparelho no payload");
        caso.Estado.Should().Be(EstadoDoCaso.RoteadoParaRevisao, "os outros sinais seguem calculáveis");
        caso.DadosIncompletos.Should().BeTrue();

        var auditoria = await AuditoriaDoCasoAsync(caso.CaseId);
        auditoria.PayloadParcial.Should().BeTrue("a marca é preservada na auditoria");

        var imeiSerie = SinalDe(auditoria, CalculadorImeiSerie.NomeDoSinal);
        imeiSerie.Estado.Should().Be(ValorSinal.Indisponivel);
        imeiSerie.Motivo.Should().Be(MotivoIndisponibilidade.DadoAusente);
    }

    [Fact]
    public async Task Tres_fontes_fora_e_fail_open_com_os_motivos_auditados()
    {
        await using var factory = new AntifraudeApiFactory(fixture, settings: new Dictionary<string, string>
        {
            ["FONTE_IMAGENS_INDISPONIVEL"] = "true",
            ["FONTE_APOLICES_INDISPONIVEL"] = "true",
            ["FONTE_HISTORICO_INDISPONIVEL"] = "true",
        });

        var caso = await PostarEProcessarAsync(factory, Payload(NovoId()));

        caso.Estado.Should().Be(EstadoDoCaso.PendenteRevisaoManual, "nenhum sinal pôde ser calculado");
        caso.Score.Should().BeNull();
        caso.DadosIncompletos.Should().BeTrue();
        caso.Rota.Should().Be(Rota.Reforcada);

        var auditoria = await AuditoriaDoCasoAsync(caso.CaseId);
        auditoria.Sinais.Should().HaveCount(3);
        auditoria.Sinais.Should().OnlyContain(s =>
            s.Estado == ValorSinal.Indisponivel && s.Motivo == MotivoIndisponibilidade.FonteIndisponivel);
    }

    [Fact]
    public async Task Reuso_de_imagem_ponta_a_ponta_aponta_o_sinistro_colidido()
    {
        await using var factory = new AntifraudeApiFactory(fixture);

        var fotoReusada = $"img://repo/reuso-{Guid.NewGuid():N}";
        var idOriginal = NovoId();
        await PostarEProcessarAsync(factory, Payload(idOriginal, fotos: [fotoReusada]));

        var casoReuso = await PostarEProcessarAsync(factory, Payload(NovoId(), fotos: [fotoReusada]));

        var auditoria = await AuditoriaDoCasoAsync(casoReuso.CaseId);
        var reuso = SinalDe(auditoria, CalculadorReusoImagem.NomeDoSinal);
        reuso.Estado.Should().Be(ValorSinal.Ativo, "a mesma referência de foto produz o mesmo pHash");
        reuso.Origem.Should().Be("phash-fake-v1", "o hash fake é sempre sinalizado");

        var evidencia = JsonSerializer.Serialize(reuso.Evidencia);
        evidencia.Should().Contain(idOriginal, "a evidência aponta o sinistro anterior colidido");
        evidencia.Should().Contain("\"distancia\":0");
    }

    [Fact]
    public async Task Velocity_ativa_no_terceiro_sinistro_do_mesmo_cliente_em_90_dias()
    {
        await using var factory = new AntifraudeApiFactory(fixture);

        var cliente = $"CLI-velocity-{Guid.NewGuid():N}";
        // IMEI exclusivo do cenário: a contagem por aparelho não sofre interferência
        // de outros testes que compartilham o mesmo MySQL.
        var aparelho = new { imei = $"35{Guid.NewGuid():N}"[..15], numeroSerie = "SN-V" };
        await PostarEProcessarAsync(factory, Payload(NovoId(), aparelho: aparelho, idCliente: cliente));
        var segundo = await PostarEProcessarAsync(factory, Payload(NovoId(), aparelho: aparelho, idCliente: cliente));
        var terceiro = await PostarEProcessarAsync(factory, Payload(NovoId(), aparelho: aparelho, idCliente: cliente));

        // 2º caso: só 1 anterior na janela → inativo (regra pede ≥2 ANTERIORES).
        var velocitySegundo = SinalDe(await AuditoriaDoCasoAsync(segundo.CaseId), CalculadorVelocity.NomeDoSinal);
        velocitySegundo.Estado.Should().Be(ValorSinal.Inativo);

        // 3º caso: 2 anteriores → ativo, com contagem e janela na evidência.
        var velocityTerceiro = SinalDe(await AuditoriaDoCasoAsync(terceiro.CaseId), CalculadorVelocity.NomeDoSinal);
        velocityTerceiro.Estado.Should().Be(ValorSinal.Ativo);

        var evidencia = JsonSerializer.Serialize(velocityTerceiro.Evidencia);
        evidencia.Should().Contain("\"contagemCliente\":2");
        evidencia.Should().Contain("\"janelaDias\":90");
    }
}
