using Antifraude.Core.Dominio;

namespace Antifraude.Infra.Mensageria;

/// <summary>Mensagem recebida da fila, com o handle para confirmação (delete) após processar.</summary>
public sealed record SinistroRecebido(Sinistro Sinistro, string ReceiptHandle);

/// <summary>
/// Porta de mensageria (infra). A API publica; o Worker consome. API e Worker não se
/// chamam direto — a comunicação é pela fila (entrada) e pelo MySQL (estado).
/// </summary>
public interface ISinistroQueue
{
    /// <summary>Cria a fila de forma idempotente no bootstrap.</summary>
    Task EnsureQueueAsync(CancellationToken ct = default);

    /// <summary>Publica um sinistro na fila.</summary>
    Task PublishAsync(Sinistro sinistro, CancellationToken ct = default);

    /// <summary>Long-poll: recebe até <paramref name="maxMensagens"/> mensagens.</summary>
    Task<IReadOnlyList<SinistroRecebido>> ReceiveAsync(int maxMensagens = 10, CancellationToken ct = default);

    /// <summary>Confirma o processamento removendo a mensagem da fila.</summary>
    Task DeleteAsync(string receiptHandle, CancellationToken ct = default);
}
