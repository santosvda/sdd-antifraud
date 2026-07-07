using Antifraude.Core.Dominio;

namespace Antifraude.Infra.Mensageria;

/// <summary>Mensagem recebida da fila, com o handle para confirmação (delete) após processar.</summary>
public sealed record SinistroRecebido(Sinistro Sinistro, string ReceiptHandle);

/// <summary>
/// Lançada quando o enfileiramento na fila principal falha após esgotar o retry com backoff.
/// Quem chama decide a escalada (fila de erro técnico) e a resposta ao produtor.
/// </summary>
public sealed class EnfileiramentoException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Porta de mensageria (infra). A API publica; o Worker consome. API e Worker não se
/// chamam direto — a comunicação é pela fila (entrada) e pelo MySQL (estado). Há duas filas:
/// a principal (processamento) e a de erro técnico (não-processáveis + escalonamento).
/// </summary>
public interface ISinistroQueue
{
    /// <summary>Cria as filas (principal + erro técnico) de forma idempotente no bootstrap.</summary>
    Task EnsureQueueAsync(CancellationToken ct = default);

    /// <summary>
    /// Publica um sinistro na fila principal, com retry/backoff (~1s/4s/16s) em falha
    /// transitória. Lança <see cref="EnfileiramentoException"/> se todas as tentativas falharem.
    /// </summary>
    Task PublishAsync(Sinistro sinistro, CancellationToken ct = default);

    /// <summary>Publica um evento na fila de erro técnico (serializado pelo adapter), carimbando o motivo.</summary>
    Task PublishErroTecnicoAsync(object corpo, string motivo, CancellationToken ct = default);

    /// <summary>Long-poll: recebe até <paramref name="maxMensagens"/> mensagens da fila principal.</summary>
    Task<IReadOnlyList<SinistroRecebido>> ReceiveAsync(int maxMensagens = 10, CancellationToken ct = default);

    /// <summary>Confirma o processamento removendo a mensagem da fila principal.</summary>
    Task DeleteAsync(string receiptHandle, CancellationToken ct = default);
}
