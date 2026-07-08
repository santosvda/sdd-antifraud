using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Antifraude.Core.Decisao;
using Antifraude.Core.Portas;
using Antifraude.Infra.Mensageria;
using Antifraude.Infra.Persistencia;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Antifraude.Tests.Integracao;

/// <summary>
/// Testes do GET /casos/{caseId} (capacidade case-read-api) e do gate de acesso por ambiente
/// (capacidade claim-console). Exercita os 3 estados de leitura, a garantia read-only e os
/// modos local/compartilhado/desabilitado do gate.
/// </summary>
[Collection(IntegrationCollection.Nome)]
public sealed class CasosReadApiTests(IntegrationFixture fixture)
{
    private static object PayloadCompleto(string idSinistro) => new
    {
        idSinistro,
        apolice = "AP-1001",
        aparelho = new { imei = "356789101112131", numeroSerie = "SN-42" },
        fotos = new[] { "img://repo/1" },
        metadados = new { abertoEm = DateTimeOffset.UtcNow, canal = "app", idCliente = "CLI-9" },
    };

    private static AntifraudeApiFactory Local(IntegrationFixture f) => new(f);

    private static async Task<Guid> PostarAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsJsonAsync("/sinistros", body);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var json = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.GetProperty("caseId").GetGuid();
    }

    private static async Task ProcessarUmaAsync(IServiceProvider sp, Guid caseId)
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

            if (await casos.ObterPorIdAsync(caseId) is not null)
            {
                return;
            }
        }
    }

    [Fact]
    public async Task Get_caso_processado_retorna_caso_e_trilhas()
    {
        await using var factory = Local(fixture);
        var client = factory.CreateClient();

        var caseId = await PostarAsync(client, PayloadCompleto($"SIN-{Guid.NewGuid():N}"));
        await ProcessarUmaAsync(factory.Services, caseId);

        var resp = await client.GetAsync($"/casos/{caseId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var raiz = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        raiz.GetProperty("encontrado").GetBoolean().Should().BeTrue();
        raiz.GetProperty("caso").GetProperty("estado").GetString().Should().Be("PendenteRevisaoManual");
        raiz.GetProperty("ingestao").GetArrayLength().Should().BeGreaterThan(0);
        raiz.GetProperty("auditoria").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Get_caso_recebido_mas_nao_processado_retorna_encontrado_false_com_ingestao()
    {
        await using var factory = Local(fixture);
        var client = factory.CreateClient();

        // Não processa a fila: o caso ainda não existe, mas a trilha de ingestão já foi carimbada.
        var caseId = await PostarAsync(client, PayloadCompleto($"SIN-{Guid.NewGuid():N}"));

        var resp = await client.GetAsync($"/casos/{caseId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var raiz = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        raiz.GetProperty("encontrado").GetBoolean().Should().BeFalse();
        raiz.GetProperty("ingestao").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Get_caso_inexistente_retorna_404()
    {
        await using var factory = Local(fixture);
        var client = factory.CreateClient();

        var resp = await client.GetAsync($"/casos/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_caso_e_somente_leitura_nao_cria_registros()
    {
        await using var factory = Local(fixture);
        var client = factory.CreateClient();

        var caseId = await PostarAsync(client, PayloadCompleto($"SIN-{Guid.NewGuid():N}"));
        await ProcessarUmaAsync(factory.Services, caseId);

        async Task<(int casos, int aud, int ing)> ContarAsync()
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AntifraudeDbContext>();
            return (
                await db.Casos.CountAsync(c => c.CaseId == caseId),
                await db.Auditoria.CountAsync(a => a.CaseId == caseId),
                await db.AuditoriaIngestao.CountAsync(a => a.CaseId == caseId));
        }

        var antes = await ContarAsync();
        for (var i = 0; i < 3; i++)
        {
            (await client.GetAsync($"/casos/{caseId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        var depois = await ContarAsync();

        depois.Should().Be(antes, "a leitura não pode criar/alterar/remover registros");
    }

    [Fact]
    public async Task Gate_desabilitado_bloqueia_console_e_casos()
    {
        await using var factory = new AntifraudeApiFactory(fixture)
            .WithWebHostBuilder(b => b.UseSetting("CONSOLE_MODO", "desabilitado"));
        var client = factory.CreateClient();

        (await client.GetAsync($"/casos/{Guid.NewGuid()}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "leitura fica 403 quando o Console está desabilitado");
        (await client.GetAsync("/")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "o Console some (404) quando desabilitado");

        // O gate não afeta a ingestão real nem o healthcheck.
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Gate_compartilhado_exige_basic_auth()
    {
        await using var factory = new AntifraudeApiFactory(fixture)
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("CONSOLE_MODO", "compartilhado");
                b.UseSetting("CONSOLE_CREDENCIAIS", "qa:secret");
            });
        var client = factory.CreateClient();

        // Sem credencial → 401.
        (await client.GetAsync($"/casos/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Credencial correta → passa o gate (404 porque o caso não existe, mas não 401).
        var req = new HttpRequestMessage(HttpMethod.Get, $"/casos/{Guid.NewGuid()}");
        req.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("qa:secret")));
        (await client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
