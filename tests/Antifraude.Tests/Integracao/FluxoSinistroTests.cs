using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antifraude.Core.Decisao;
using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;
using Antifraude.Infra.Mensageria;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Antifraude.Tests.Integracao;

[Collection(IntegrationCollection.Nome)]
public sealed class FluxoSinistroTests(IntegrationFixture fixture)
{
    private static object PayloadCompleto(string idSinistro) => new
    {
        idSinistro,
        apolice = "AP-1001",
        aparelho = new { imei = "356789101112131", numeroSerie = "SN-42" },
        fotos = new[] { "img://repo/1", "img://repo/2" },
        metadados = new { abertoEm = DateTimeOffset.UtcNow, canal = "app", idCliente = "CLI-9" },
    };

    /// <summary>Consome UMA mensagem da fila principal e a processa como o Worker faria.</summary>
    private static async Task<Caso?> ProcessarUmaAsync(IServiceProvider sp, Guid caseId)
    {
        for (var tentativa = 0; tentativa < 5; tentativa++)
        {
            using var scope = sp.CreateScope();
            var s = scope.ServiceProvider;
            var queue = s.GetRequiredService<ISinistroQueue>();
            var motor = s.GetRequiredService<MotorDeDecisao>();
            var casos = s.GetRequiredService<ICaseRepository>();
            var auditoria = s.GetRequiredService<IAuditLog>();

            foreach (var recebido in await queue.ReceiveAsync())
            {
                var resultado = await motor.AvaliarAsync(recebido.Sinistro);
                await casos.SalvarAsync(resultado.Caso);
                await auditoria.RegistrarAsync(resultado.Auditoria);
                await queue.DeleteAsync(recebido.ReceiptHandle);
            }

            var caso = await casos.ObterPorIdAsync(caseId);
            if (caso is not null)
            {
                return caso;
            }
        }

        return null;
    }

    private static async Task<(HttpStatusCode Status, Guid CaseId)> PostarAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsJsonAsync("/sinistros", body);
        Guid caseId = default;
        if (resp.StatusCode == HttpStatusCode.Accepted)
        {
            var json = await resp.Content.ReadAsStringAsync();
            caseId = JsonDocument.Parse(json).RootElement.GetProperty("caseId").GetGuid();
        }

        return (resp.StatusCode, caseId);
    }

    [Fact]
    public async Task Fumaca_post_sinistro_real_sem_sinais_vira_pendente_revisao_manual()
    {
        await using var factory = new AntifraudeApiFactory(fixture);
        var client = factory.CreateClient();

        var (status, caseId) = await PostarAsync(client, PayloadCompleto($"SIN-{Guid.NewGuid():N}"));
        status.Should().Be(HttpStatusCode.Accepted);

        var caso = await ProcessarUmaAsync(factory.Services, caseId);

        // Sem coleta de sinais (2.2 ainda não existe), o fail-open roteia para revisão manual.
        caso.Should().NotBeNull();
        caso!.Estado.Should().Be(EstadoDoCaso.PendenteRevisaoManual);
        caso.Score.Should().BeNull();
        caso.Faixa.Should().Be(Faixa.Indeterminado);
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
