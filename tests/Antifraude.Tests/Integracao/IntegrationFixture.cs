using Testcontainers.LocalStack;
using Testcontainers.MySql;
using Xunit;

namespace Antifraude.Tests.Integracao;

/// <summary>
/// Sobe MySQL + LocalStack (SQS) via Testcontainers uma vez por coleção de testes.
/// Os cenários Gherkin do PRD viram testes de integração sobre estes containers.
/// </summary>
public sealed class IntegrationFixture : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("antifraude")
        .WithUsername("root")
        .WithPassword("root")
        .Build();

    private readonly LocalStackContainer _localstack = new LocalStackBuilder("localstack/localstack:3")
        .WithEnvironment("SERVICES", "sqs")
        .Build();

    public string ConnectionString => _mysql.GetConnectionString();

    public string SqsServiceUrl => _localstack.GetConnectionString();

    public string Region => "us-east-1";

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_mysql.StartAsync(), _localstack.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await _mysql.DisposeAsync();
        await _localstack.DisposeAsync();
    }
}

[CollectionDefinition(Nome)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationFixture>
{
    public const string Nome = "integracao";
}
