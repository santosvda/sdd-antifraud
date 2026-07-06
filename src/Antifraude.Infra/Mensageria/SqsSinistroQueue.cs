using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Antifraude.Core.Dominio;
using Microsoft.Extensions.Logging;

namespace Antifraude.Infra.Mensageria;

/// <summary>
/// Adapter SQS (AWSSDK) de <see cref="ISinistroQueue"/>. Endpoint/credenciais/região vêm
/// de <see cref="SqsOptions"/> (env var na borda). A fila é criada de forma idempotente
/// no bootstrap para não depender de setup manual.
/// </summary>
public sealed class SqsSinistroQueue(IAmazonSQS sqs, SqsOptions options, ILogger<SqsSinistroQueue> logger)
    : ISinistroQueue
{
    private string? _queueUrl;

    public async Task EnsureQueueAsync(CancellationToken ct = default)
    {
        // CreateQueue é idempotente: retorna a URL existente se a fila já existe.
        var resp = await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = options.QueueName }, ct)
            .ConfigureAwait(false);
        _queueUrl = resp.QueueUrl;
        logger.LogInformation("Fila SQS pronta: {QueueName} ({QueueUrl})", options.QueueName, _queueUrl);
    }

    public async Task PublishAsync(Sinistro sinistro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sinistro);
        var url = await ResolverUrlAsync(ct).ConfigureAwait(false);
        var body = JsonSerializer.Serialize(sinistro);
        await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = url, MessageBody = body }, ct)
            .ConfigureAwait(false);
        logger.LogInformation("Sinistro publicado na fila. caseId={CaseId}", sinistro.CaseId);
    }

    public async Task<IReadOnlyList<SinistroRecebido>> ReceiveAsync(int maxMensagens = 10, CancellationToken ct = default)
    {
        var url = await ResolverUrlAsync(ct).ConfigureAwait(false);
        var resp = await sqs.ReceiveMessageAsync(
            new ReceiveMessageRequest
            {
                QueueUrl = url,
                MaxNumberOfMessages = Math.Clamp(maxMensagens, 1, 10),
                WaitTimeSeconds = options.WaitTimeSeconds,
            }, ct).ConfigureAwait(false);

        var recebidos = new List<SinistroRecebido>();
        foreach (var msg in resp.Messages ?? [])
        {
            var sinistro = JsonSerializer.Deserialize<Sinistro>(msg.Body);
            if (sinistro is null)
            {
                logger.LogWarning("Mensagem SQS ignorada (corpo inválido). messageId={MessageId}", msg.MessageId);
                continue;
            }

            recebidos.Add(new SinistroRecebido(sinistro, msg.ReceiptHandle));
        }

        return recebidos;
    }

    public async Task DeleteAsync(string receiptHandle, CancellationToken ct = default)
    {
        var url = await ResolverUrlAsync(ct).ConfigureAwait(false);
        await sqs.DeleteMessageAsync(new DeleteMessageRequest { QueueUrl = url, ReceiptHandle = receiptHandle }, ct)
            .ConfigureAwait(false);
    }

    private async Task<string> ResolverUrlAsync(CancellationToken ct)
    {
        if (_queueUrl is not null)
        {
            return _queueUrl;
        }

        await EnsureQueueAsync(ct).ConfigureAwait(false);
        return _queueUrl!;
    }
}
