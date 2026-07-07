namespace Antifraude.Infra.Mensageria;

/// <summary>
/// Política de retry com backoff para o enfileiramento. Executa a ação; em falha transitória,
/// espera o próximo backoff e tenta de novo. Esgotados os backoffs, lança
/// <see cref="EnfileiramentoException"/> agregando a última falha. Cancelamento não é retry.
/// </summary>
public static class PoliticaRetry
{
    public static async Task ExecutarComBackoffAsync(
        Func<CancellationToken, Task> acao,
        IReadOnlyList<TimeSpan> backoffs,
        TimeProvider clock,
        Action<Exception, int>? aoFalhar = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(acao);
        ArgumentNullException.ThrowIfNull(backoffs);

        Exception? ultima = null;
        for (var tentativa = 0; tentativa < backoffs.Count; tentativa++)
        {
            try
            {
                await acao(ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ultima = ex;
                aoFalhar?.Invoke(ex, tentativa);
                await Task.Delay(backoffs[tentativa], clock, ct).ConfigureAwait(false);
            }
        }

        throw new EnfileiramentoException($"Enfileiramento falhou após {backoffs.Count} tentativas.", ultima);
    }
}
