namespace Antifraude.Infra.Mensageria;

/// <summary>
/// Configuração do SQS, lida de env var na composição (borda). Aponta para o LocalStack
/// em dev; em produção AWS só o endpoint/credenciais mudam — o código é o mesmo.
/// </summary>
public sealed class SqsOptions
{
    /// <summary>Endpoint do serviço (ex.: <c>http://localstack:4566</c>). Vazio = AWS real.</summary>
    public string? ServiceUrl { get; set; }

    public string Region { get; set; } = "us-east-1";

    public string QueueName { get; set; } = "sinistros";

    public string AccessKey { get; set; } = "test";

    public string SecretKey { get; set; } = "test";

    /// <summary>Segundos de long-poll no recebimento (0–20).</summary>
    public int WaitTimeSeconds { get; set; } = 20;
}
