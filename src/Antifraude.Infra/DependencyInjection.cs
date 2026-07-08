using Amazon.Runtime;
using Amazon.SQS;
using Antifraude.Core.Coleta;
using Antifraude.Core.Decisao;
using Antifraude.Core.Portas;
using Antifraude.Infra.Fontes;
using Antifraude.Infra.Mensageria;
using Antifraude.Infra.Persistencia;
using Antifraude.Infra.Score;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        // A factory existe porque os calculadores de sinal rodam em PARALELO e o
        // DbContext não é thread-safe: cada fonte abre seu próprio contexto curto.
        services.AddDbContextFactory<AntifraudeDbContext>(opt =>
            opt.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0))));
        // Convive com a factory: as options precisam ser singleton para ambos.
        services.AddDbContext<AntifraudeDbContext>(
            (Action<DbContextOptionsBuilder>?)null,
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);

        services.AddScoped<ICaseRepository, CaseRepository>();
        services.AddScoped<IScoringConfigRepository, ScoringConfigRepository>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAuditLogIngestao, AuditLogIngestao>();
        services.AddScoped<ISinistroDedupStore, SinistroDedupStore>();

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

        // Coleta de sinais (feature 2.2): fontes fake no MySQL atrás de decorators de
        // resiliência (timeout + circuit breaker por fonte, estado singleton) e os 3
        // calculadores orquestrados pelo ColetorDeSinais.
        var timeout = TimeSpan.FromSeconds(double.TryParse(config["FONTE_TIMEOUT_SEGUNDOS"], out var ts) ? ts : 5);
        var falhasParaAbrir = int.TryParse(config["FONTE_BREAKER_FALHAS"], out var f) ? f : 3;
        var duracaoAberto = TimeSpan.FromSeconds(double.TryParse(config["FONTE_BREAKER_ABERTO_SEGUNDOS"], out var da) ? da : 30);

        FonteResilienteOptions OpcoesDaFonte(string envIndisponivel) => new()
        {
            Timeout = timeout,
            FalhasParaAbrir = falhasParaAbrir,
            DuracaoCircuitoAberto = duracaoAberto,
            SimularIndisponibilidade =
                bool.TryParse(config[envIndisponivel], out var fora) && fora,
        };

        CircuitoDaFonte NovoCircuito(IServiceProvider sp, string nome, string envIndisponivel) => new(
            nome,
            OpcoesDaFonte(envIndisponivel),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger($"Fontes.{nome}"),
            sp.GetRequiredService<TimeProvider>());

        services.AddKeyedSingleton("imagens", (sp, _) => NovoCircuito(sp, "imagens", "FONTE_IMAGENS_INDISPONIVEL"));
        services.AddKeyedSingleton("apolices", (sp, _) => NovoCircuito(sp, "apolices", "FONTE_APOLICES_INDISPONIVEL"));
        services.AddKeyedSingleton("historico", (sp, _) => NovoCircuito(sp, "historico", "FONTE_HISTORICO_INDISPONIVEL"));

        services.AddScoped<IRepositorioDeImagens>(sp => new RepositorioDeImagensResiliente(
            ActivatorUtilities.CreateInstance<RepositorioDeImagensMySql>(sp),
            sp.GetRequiredKeyedService<CircuitoDaFonte>("imagens")));
        services.AddScoped<IBaseDeApolices>(sp => new BaseDeApolicesResiliente(
            ActivatorUtilities.CreateInstance<BaseDeApolicesMySql>(sp),
            sp.GetRequiredKeyedService<CircuitoDaFonte>("apolices")));
        services.AddScoped<IHistoricoDeSinistros>(sp => new HistoricoDeSinistrosResiliente(
            ActivatorUtilities.CreateInstance<HistoricoDeSinistrosMySql>(sp),
            sp.GetRequiredKeyedService<CircuitoDaFonte>("historico")));

        services.AddScoped<ICalculadorDeSinal, CalculadorReusoImagem>();
        services.AddScoped<ICalculadorDeSinal, CalculadorImeiSerie>();
        services.AddScoped<ICalculadorDeSinal, CalculadorVelocity>();
        services.AddScoped<ColetorDeSinais>();

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
