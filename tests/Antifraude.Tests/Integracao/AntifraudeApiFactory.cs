using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Antifraude.Tests.Integracao;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> da API apontando para os containers do
/// <see cref="IntegrationFixture"/>. Sobe a API real (migrations + fila no start).
/// </summary>
public sealed class AntifraudeApiFactory(IntegrationFixture fixture, bool providerIndisponivel = false)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("MYSQL_CONNECTION_STRING", fixture.ConnectionString);
        builder.UseSetting("SQS_SERVICE_URL", fixture.SqsServiceUrl);
        builder.UseSetting("SQS_QUEUE_NAME", $"sinistros-{Guid.NewGuid():N}");
        builder.UseSetting("AWS_REGION", fixture.Region);
        builder.UseSetting("AWS_ACCESS_KEY_ID", "test");
        builder.UseSetting("AWS_SECRET_ACCESS_KEY", "test");
        builder.UseSetting("MOCK_SCORE_PROVIDER_INDISPONIVEL", providerIndisponivel ? "true" : "false");
    }
}
