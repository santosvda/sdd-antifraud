using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Antifraude.Core.Dominio;
using Microsoft.Extensions.Logging;

namespace Antifraude.Infra.Mensageria;

/// <summary>
/// Adapter SQS (AWSSDK) de <see cref="ISinistroQueue"/>. Endpoint/credenciais/região vêm
/// de <see cref="SqsOptions"/> (env var na borda). As filas (principal + erro técnico) são
/// criadas de forma idempotente no bootstrap. A publicação na fila principal aplica retry com
/// backoff exponencial; ao esgotar, lança <see cref="EnfileiramentoException"/> para quem
/// chama escalar para a fila de erro técnico.
/// </summary>
public sealed class SqsSinistroQueue(
    IAmazonSQS sqs,
    SqsOptions options,
    ILogger<SqsSinistroQueue> logger,
    TimeProvider? timeProvider = null)
    : ISinistroQueue
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private string? _queueUrl;
    private string? _errorQueueUrl;

    public async Task EnsureQueueAsync(CancellationToken ct = default)
    {
        // CreateQueue é idempotente: retorna a URL existente se a fila já existe.
        _queueUrl = await CriarFilaAsync(options.QueueName, ct).ConfigureAwait(false);
        _errorQueueUrl = await CriarFilaAsync(options.ErrorQueueName, ct).ConfigureAwait(false);
    }

    public async Task PublishAsync(Sinistro sinistro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sinistro);
        var url = await ResolverUrlAsync(ct).ConfigureAwait(false);
        var body = JsonSerializer.Serialize(sinistro);

        await PoliticaRetry.ExecutarComBackoffAsync(
            async token => await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = url, MessageBody = body }, token)
                .ConfigureAwait(false),
            options.Backoffs,
            _clock,
            (ex, tentativa) => logger.LogWarning(
                ex, "Falha ao enfileirar (tentativa {Tentativa}/{Total}). caseId={CaseId}",
                tentativa + 1, options.Backoffs.Count, sinistro.CaseId),
            ct).ConfigureAwait(false);

        logger.LogInformation("Sinistro publicado na fila. caseId={CaseId}", sinistro.CaseId);
    }

    public async Task PublishErroTecnicoAsync(object corpo, string motivo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(corpo);
        var url = await ResolverErrorUrlAsync(ct).ConfigureAwait(false);
        var req = new SendMessageRequest
        {
            QueueUrl = url,
            MessageBody = JsonSerializer.Serialize(corpo),
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["motivo"] = new() { DataType = "String", StringValue = motivo },
            },
        };
        await sqs.SendMessageAsync(req, ct).ConfigureAwait(false);
        logger.LogWarning("Evento roteado para a fila de erro técnico. motivo={Motivo}", motivo);
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

    private async Task<string> CriarFilaAsync(string nome, CancellationToken ct)
    {
        var resp = await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = nome }, ct).ConfigureAwait(false);
        logger.LogInformation("Fila SQS pronta: {QueueName} ({QueueUrl})", nome, resp.QueueUrl);
        return resp.QueueUrl;
    }

    private async Task<string> ResolverUrlAsync(CancellationToken ct) =>
        _queueUrl ??= await CriarFilaAsync(options.QueueName, ct).ConfigureAwait(false);

    private async Task<string> ResolverErrorUrlAsync(CancellationToken ct) =>
        _errorQueueUrl ??= await CriarFilaAsync(options.ErrorQueueName, ct).ConfigureAwait(false);
}
