using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Antifraude.Tests.Integracao;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> da API apontando para os containers do
/// <see cref="IntegrationFixture"/>. Sobe a API real (migrations + filas no start).
/// O <paramref name="configurarServicos"/> permite trocar adapters (ex.: forçar o store de
/// dedup a cair) para exercitar caminhos de fail-open.
/// </summary>
public sealed class AntifraudeApiFactory(
    IntegrationFixture fixture,
    bool providerIndisponivel = false,
    Action<IServiceCollection>? configurarServicos = null,
    IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var sufixo = Guid.NewGuid().ToString("N");
        builder.UseSetting("MYSQL_CONNECTION_STRING", fixture.ConnectionString);
        builder.UseSetting("SQS_SERVICE_URL", fixture.SqsServiceUrl);
        builder.UseSetting("SQS_QUEUE_NAME", $"sinistros-{sufixo}");
        builder.UseSetting("SQS_ERROR_QUEUE_NAME", $"sinistros-erro-{sufixo}");
        builder.UseSetting("AWS_REGION", fixture.Region);
        builder.UseSetting("AWS_ACCESS_KEY_ID", "test");
        builder.UseSetting("AWS_SECRET_ACCESS_KEY", "test");
        builder.UseSetting("MOCK_SCORE_PROVIDER_INDISPONIVEL", providerIndisponivel ? "true" : "false");

        foreach (var (chave, valor) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(chave, valor);
        }

        if (configurarServicos is not null)
        {
            builder.ConfigureServices(configurarServicos);
        }
    }
}
