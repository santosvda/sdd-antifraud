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
    /// <summary>
    /// Consome uma mensagem da fila e a processa como o Worker faria (motor + persistência).
    /// Exercita os adapters reais (SQS receive + MySQL) — a segunda metade do fluxo.
    /// </summary>
    private static async Task<Caso?> ProcessarUmaAsync(IServiceProvider sp, Guid caseId)
    {
        using var scope = sp.CreateScope();
        var s = scope.ServiceProvider;
        var queue = s.GetRequiredService<ISinistroQueue>();
        var motor = s.GetRequiredService<MotorDeDecisao>();
        var casos = s.GetRequiredService<ICaseRepository>();
        var auditoria = s.GetRequiredService<IAuditLog>();

        // Long-poll até achar a mensagem (tolera ordem/latência do LocalStack).
        for (var tentativa = 0; tentativa < 5; tentativa++)
        {
            var mensagens = await queue.ReceiveAsync();
            foreach (var recebido in mensagens)
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

    private static async Task<Guid> PostarSinistroAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsJsonAsync("/sinistros", body);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var json = await resp.Content.ReadAsStringAsync();
        var caseId = JsonDocument.Parse(json).RootElement.GetProperty("caseId").GetGuid();
        return caseId;
    }

    [Fact]
    public async Task Fumaca_post_sinistros_resulta_em_caso_roteado_persistido()
    {
        await using var factory = new AntifraudeApiFactory(fixture);
        var client = factory.CreateClient();

        var caseId = await PostarSinistroAsync(client, new
        {
            sinais = new[]
            {
                new { nome = "reuso_imagem", valor = 1.0, origem = "mock" },
                new { nome = "imei_serie_divergente", valor = 1.0, origem = "mock" },
            },
        });

        var caso = await ProcessarUmaAsync(factory.Services, caseId);

        caso.Should().NotBeNull();
        caso!.CaseId.Should().Be(caseId);
        caso.Estado.Should().Be(EstadoDoCaso.RoteadoParaRevisao);
        caso.Rota.Should().BeOneOf(Rota.Normal, Rota.Reforcada);
        caso.VersaoProvider.Should().Be("mock-v1", "o score veio de um provider mock sinalizado");
        caso.VersaoConfig.Should().Be(1);
    }

    [Fact]
    public async Task Payload_invalido_e_rejeitado_com_400_e_nao_enfileira()
    {
        await using var factory = new AntifraudeApiFactory(fixture);
        var client = factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/sinistros", new
        {
            sinais = new[] { new { nome = "", valor = 9.0 } },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Provider_indisponivel_nao_bloqueia_sinistro_vira_pendente_revisao_manual()
    {
        // Mock em modo "simular queda" — prova o fail-open / não-bloqueio.
        await using var factory = new AntifraudeApiFactory(fixture, providerIndisponivel: true);
        var client = factory.CreateClient();

        var caseId = await PostarSinistroAsync(client, new
        {
            sinais = new[] { new { nome = "reuso_imagem", valor = 1.0, origem = "mock" } },
        });

        var caso = await ProcessarUmaAsync(factory.Services, caseId);

        // O sinistro SEGUE: o caso nasce e fica visível, nunca bloqueado.
        caso.Should().NotBeNull();
        caso!.Estado.Should().Be(EstadoDoCaso.PendenteRevisaoManual);
        caso.Faixa.Should().Be(Faixa.Indeterminado);
        caso.Score.Should().BeNull();
    }
}
