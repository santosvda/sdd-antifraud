using System.Net;
using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Antifraude.Tests.Integracao;

[Collection(IntegrationCollection.Nome)]
public sealed class FluxoSinistroTests(IntegrationFixture fixture)
{
    /// <summary>
    /// Payload completo com identificadores ÚNICOS por chamada — evita colisão de
    /// reuso de imagem/velocity entre testes que compartilham o mesmo MySQL.
    /// </summary>
    private static object PayloadCompleto(string idSinistro) => new
    {
        idSinistro,
        apolice = $"AP-{idSinistro}",
        aparelho = new { imei = $"35{Guid.NewGuid():N}"[..15], numeroSerie = $"SN-{idSinistro}" },
        fotos = new[] { $"img://repo/{idSinistro}/1", $"img://repo/{idSinistro}/2" },
        metadados = new { abertoEm = DateTimeOffset.UtcNow, canal = "app", idCliente = $"CLI-{idSinistro}" },
    };

    private static Task<Caso?> ProcessarUmaAsync(IServiceProvider sp, Guid caseId) =>
        WorkerSimulado.ProcessarUmaAsync(sp, caseId);

    private static Task<(HttpStatusCode Status, Guid CaseId)> PostarAsync(HttpClient client, object body) =>
        WorkerSimulado.PostarAsync(client, body);

    [Fact]
    public async Task Fumaca_post_sinistro_real_com_coleta_ganha_score_e_roteia_para_revisao()
    {
        await using var factory = new AntifraudeApiFactory(fixture);
        var client = factory.CreateClient();

        var (status, caseId) = await PostarAsync(client, PayloadCompleto($"SIN-{Guid.NewGuid():N}"));
        status.Should().Be(HttpStatusCode.Accepted);

        var caso = await ProcessarUmaAsync(factory.Services, caseId);

        // Com a coleta de sinais (2.2), o payload completo produz sinais calculados e
        // o caso segue o fluxo normal de score — sempre para revisão humana.
        caso.Should().NotBeNull();
        caso!.Estado.Should().Be(EstadoDoCaso.RoteadoParaRevisao);
        caso.Score.Should().NotBeNull("os 3 sinais foram calculados");
        caso.PayloadParcial.Should().BeFalse("o payload estava completo");
    }

    [Fact]
    public async Task Corpo_ilegivel_e_rejeitado_com_400()
    {
        await using var factory = new AntifraudeApiFactory(fixture);
        var client = factory.CreateClient();

        var resp = await client.PostAsync("/sinistros",
            new StringContent("{ isto não é json", System.Text.Encoding.UTF8, "application/json"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sem_idSinistro_retorna_202_e_nao_enfileira_no_processamento()
    {
        await using var factory = new AntifraudeApiFactory(fixture);
        var client = factory.CreateClient();

        // JSON válido, mas sem idSinistro → 202 (não 400) + roteado para a fila de erro técnico.
        var (status, caseId) = await PostarAsync(client, new { apolice = "AP-1", fotos = new[] { "img://1" } });
        status.Should().Be(HttpStatusCode.Accepted);

        // Não deve haver caso processável na fila principal para este caseId.
        var caso = await ProcessarUmaAsync(factory.Services, caseId);
        caso.Should().BeNull("o evento sem idSinistro foi para a fila de erro técnico, não para processamento");
    }

    [Fact]
    public async Task Payload_parcial_e_enfileirado_e_marcado()
    {
        await using var factory = new AntifraudeApiFactory(fixture);
        var client = factory.CreateClient();

        // idSinistro presente, sem aparelho → payload parcial, segue enfileirado.
        var (status, caseId) = await PostarAsync(client, new
        {
            idSinistro = $"SIN-{Guid.NewGuid():N}",
            apolice = "AP-1",
            fotos = new[] { "img://1" },
            metadados = new { canal = "app", idCliente = "C1" },
        });
        status.Should().Be(HttpStatusCode.Accepted);

        var caso = await ProcessarUmaAsync(factory.Services, caseId);
        caso.Should().NotBeNull();
        caso!.PayloadParcial.Should().BeTrue("faltou o aparelho no payload");
    }

    [Fact]
    public async Task Evento_duplicado_em_24h_e_descartado_sem_segunda_entrada()
    {
        await using var factory = new AntifraudeApiFactory(fixture);
        var client = factory.CreateClient();

        var id = $"SIN-{Guid.NewGuid():N}";
        var (s1, caseId1) = await PostarAsync(client, PayloadCompleto(id));
        var (s2, caseId2) = await PostarAsync(client, PayloadCompleto(id));

        s1.Should().Be(HttpStatusCode.Accepted);
        s2.Should().Be(HttpStatusCode.Accepted);

        // O primeiro é enfileirado; o segundo é descartado por idempotência (não reenfileira).
        var caso1 = await ProcessarUmaAsync(factory.Services, caseId1);
        var caso2 = await ProcessarUmaAsync(factory.Services, caseId2);

        caso1.Should().NotBeNull();
        caso2.Should().BeNull("o duplicado não gerou nova entrada na fila de processamento");
    }

    [Fact]
    public async Task Store_de_dedup_indisponivel_nao_bloqueia_o_sinistro()
    {
        await using var factory = new AntifraudeApiFactory(fixture, configurarServicos: services =>
        {
            services.AddScoped<ISinistroDedupStore, DedupStoreQuebrado>();
        });
        var client = factory.CreateClient();

        var (status, caseId) = await PostarAsync(client, PayloadCompleto($"SIN-{Guid.NewGuid():N}"));

        // Fail-open: mesmo com o store fora, o sinistro é aceito e enfileirado.
        status.Should().Be(HttpStatusCode.Accepted);
        var caso = await ProcessarUmaAsync(factory.Services, caseId);
        caso.Should().NotBeNull("a queda do store de dedup não pode bloquear o enfileiramento");
    }
}

/// <summary>Store de dedup que sempre falha — para exercitar o fail-open da ingestão.</summary>
internal sealed class DedupStoreQuebrado : ISinistroDedupStore
{
    public Task<bool> RegistrarSeNovoAsync(string idSinistro, CancellationToken ct = default) =>
        throw new InvalidOperationException("store de dedup indisponível (simulado)");

    public Task<int> PurgarExpiradosAsync(CancellationToken ct = default) => Task.FromResult(0);
}
