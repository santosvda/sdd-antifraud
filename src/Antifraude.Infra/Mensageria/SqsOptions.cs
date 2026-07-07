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

    /// <summary>Fila de erro técnico (DLQ aplicacional): eventos não-processáveis + escalonamento.</summary>
    public string ErrorQueueName { get; set; } = "sinistros-erro-tecnico";

    public string AccessKey { get; set; } = "test";

    public string SecretKey { get; set; } = "test";

    /// <summary>Segundos de long-poll no recebimento (0–20).</summary>
    public int WaitTimeSeconds { get; set; } = 20;

    /// <summary>Backoff entre tentativas de enfileiramento (~1s/4s/16s, ~21s total). Configurável p/ testes.</summary>
    public IReadOnlyList<TimeSpan> Backoffs { get; set; } =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(16),
    ];
}
