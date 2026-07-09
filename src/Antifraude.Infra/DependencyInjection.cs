using Amazon.Runtime;
using Amazon.SQS;
using Antifraude.Core.Decisao;
using Antifraude.Core.Portas;
using Antifraude.Infra.Alertas;
using Antifraude.Infra.Mensageria;
using Antifraude.Infra.Persistencia;
using Antifraude.Infra.Score;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Antifraude.Infra;

/// <summary>
/// Composição da infraestrutura (borda). Api e Worker chamam <see cref="AddAntifraudeInfra"/>
/// e ganham DbContext, repositórios, auditoria, provider mock, fila SQS e o
/// <see cref="MotorDeDecisao"/> do Core. Toda configuração vem de env var / IConfiguration.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAntifraudeInfra(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config["MYSQL_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("MYSQL_CONNECTION_STRING não configurada.");

        // Versão fixada (evita uma conexão bloqueante de autodetecção no start).
        services.AddDbContext<AntifraudeDbContext>(opt =>
            opt.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0))));

        services.AddScoped<ICaseRepository, CaseRepository>();
        services.AddScoped<IScoringConfigRepository, ScoringConfigRepository>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAuditLogIngestao, AuditLogIngestao>();
        services.AddScoped<ISinistroDedupStore, SinistroDedupStore>();

        // Alerta técnico (distinto do operacional): adapter de log Critical na fundação.
        services.AddSingleton<IAlertaTecnico, AlertaTecnicoLog>();

        // Provider de score: mock sinalizado nesta fundação (fatia 1 troca a implementação).
        var mockOptions = new MockScoreProviderOptions
        {
            SimularIndisponibilidade =
                bool.TryParse(config["MOCK_SCORE_PROVIDER_INDISPONIVEL"], out var indisponivel) && indisponivel,
        };
        services.AddSingleton(mockOptions);
        services.AddSingleton<IScoreProvider, MockScoreProvider>(); // sem estado por requisição

        // Pipeline de decisão (Core). Escopo porque depende do repositório scoped.
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<MotorDeDecisao>();

        // Mensageria SQS (LocalStack em dev, AWS em prod — só muda o endpoint).
        var sqsOptions = new SqsOptions
        {
            ServiceUrl = config["SQS_SERVICE_URL"],
            Region = config["AWS_REGION"] ?? "us-east-1",
            QueueName = config["SQS_QUEUE_NAME"] ?? "sinistros",
            ErrorQueueName = config["SQS_ERROR_QUEUE_NAME"] ?? $"{config["SQS_QUEUE_NAME"] ?? "sinistros"}-erro-tecnico",
            AccessKey = config["AWS_ACCESS_KEY_ID"] ?? "test",
            SecretKey = config["AWS_SECRET_ACCESS_KEY"] ?? "test",
        };
        services.AddSingleton(sqsOptions);
        services.AddSingleton<IAmazonSQS>(_ =>
        {
            var sqsConfig = new AmazonSQSConfig { RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(sqsOptions.Region) };
            if (!string.IsNullOrWhiteSpace(sqsOptions.ServiceUrl))
            {
                sqsConfig.ServiceURL = sqsOptions.ServiceUrl;
                sqsConfig.AuthenticationRegion = sqsOptions.Region;
            }

            var creds = new BasicAWSCredentials(sqsOptions.AccessKey, sqsOptions.SecretKey);
            return new AmazonSQSClient(creds, sqsConfig);
        });
        services.AddSingleton<ISinistroQueue, SqsSinistroQueue>();

        return services;
    }
}
