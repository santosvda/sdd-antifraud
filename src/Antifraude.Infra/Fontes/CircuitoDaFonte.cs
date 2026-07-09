using Microsoft.Extensions.Logging;

namespace Antifraude.Infra.Fontes;

/// <summary>Configuração de resiliência de uma fonte de dados (env vars <c>FONTE_*</c>).</summary>
public sealed class FonteResilienteOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public int FalhasParaAbrir { get; init; } = 3;

    public TimeSpan DuracaoCircuitoAberto { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Simula a fonte fora do ar (demo/testes) — toda chamada falha imediatamente.</summary>
    public bool SimularIndisponibilidade { get; init; }
}

/// <summary>
/// Timeout + circuit breaker de UMA fonte de dados, independente das demais (RF06 do
/// PRD 2.2). Estado singleton — o circuito atravessa escopos/mensagens: N falhas
/// consecutivas abrem o circuito por T segundos e as chamadas seguintes falham imediato
/// (viram sinal "indisponível" no calculador), sem esperar timeout. Loga a latência por
/// operação para as métricas de disponibilidade por sinal.
/// </summary>
public sealed class CircuitoDaFonte(
    string nome,
    FonteResilienteOptions options,
    ILogger logger,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly Lock _lock = new();

    private int _falhasConsecutivas;
    private DateTimeOffset? _abertoAte;

    public async Task<T> ExecutarAsync<T>(
        string operacao, Func<CancellationToken, Task<T>> acao, CancellationToken ct)
    {
        if (options.SimularIndisponibilidade)
        {
            throw new InvalidOperationException($"Fonte '{nome}' em modo 'simular indisponibilidade'.");
        }

        var agora = _clock.GetUtcNow();
        lock (_lock)
        {
            if (_abertoAte is { } abertoAte && abertoAte > agora)
            {
                throw new InvalidOperationException(
                    $"Circuito da fonte '{nome}' aberto até {abertoAte:O} — chamada não realizada.");
            }
        }

        var inicio = _clock.GetTimestamp();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(options.Timeout);
        try
        {
            var resultado = await acao(cts.Token).ConfigureAwait(false);
            lock (_lock)
            {
                _falhasConsecutivas = 0;
                _abertoAte = null;
            }

            logger.LogDebug(
                "Fonte consultada. fonte={Fonte} operacao={Operacao} latenciaMs={LatenciaMs}",
                nome, operacao, _clock.GetElapsedTime(inicio).TotalMilliseconds);
            return resultado;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            RegistrarFalha(operacao, $"timeout de {options.Timeout.TotalSeconds:0.#}s");
            throw new TimeoutException(
                $"Fonte '{nome}' excedeu o timeout de {options.Timeout.TotalSeconds:0.#}s em '{operacao}'.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RegistrarFalha(operacao, ex.Message);
            throw;
        }
    }

    public Task ExecutarAsync(string operacao, Func<CancellationToken, Task> acao, CancellationToken ct) =>
        ExecutarAsync(operacao, async token =>
        {
            await acao(token).ConfigureAwait(false);
            return true;
        }, ct);

    private void RegistrarFalha(string operacao, string motivo)
    {
        lock (_lock)
        {
            _falhasConsecutivas++;
            if (_falhasConsecutivas >= options.FalhasParaAbrir)
            {
                _abertoAte = _clock.GetUtcNow() + options.DuracaoCircuitoAberto;
                logger.LogWarning(
                    "Circuito aberto. fonte={Fonte} falhasConsecutivas={Falhas} abertoAte={AbertoAte}",
                    nome, _falhasConsecutivas, _abertoAte);
            }
        }

        logger.LogWarning(
            "Falha na fonte. fonte={Fonte} operacao={Operacao} motivo={Motivo}",
            nome, operacao, motivo);
    }
}
