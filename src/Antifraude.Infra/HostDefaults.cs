using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Antifraude.Infra;

/// <summary>
/// Convenções de host compartilhadas por Api e Worker: config por env var, logs
/// estruturados JSON com scopes (correlação por <c>caseId</c>) e a composição da infra.
/// </summary>
public static class HostDefaults
{
    public static TBuilder AddAntifraudeHostDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Configuration.AddEnvironmentVariables();

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

        builder.Services.AddAntifraudeInfra(builder.Configuration);

        return builder;
    }
}
