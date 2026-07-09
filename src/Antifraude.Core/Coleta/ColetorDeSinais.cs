using Antifraude.Core.Dominio;

namespace Antifraude.Core.Coleta;

/// <summary>
/// Orquestra os calculadores de sinal em PARALELO (<see cref="Task.WhenAll{TResult}(IEnumerable{Task{TResult}})"/>).
///
/// Guardrail do PRD 2.2 materializado aqui: a falha de um calculador afeta apenas o
/// sinal correspondente — nunca os demais, nunca o caso. Os calculadores já convertem
/// dado ausente/fonte fora em <see cref="ValorSinal.Indisponivel"/>; este coletor é a
/// última linha de defesa caso algum lance mesmo assim. A saída SEMPRE contém um sinal
/// por calculador registrado.
/// </summary>
public sealed class ColetorDeSinais(
    IEnumerable<ICalculadorDeSinal> calculadores,
    TimeProvider? timeProvider = null)
{
    private readonly IReadOnlyList<ICalculadorDeSinal> _calculadores = [.. calculadores];
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<Sinal>> ColetarAsync(Sinistro sinistro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sinistro);
        return await Task.WhenAll(_calculadores.Select(c => CalcularIsoladoAsync(c, sinistro, ct)))
            .ConfigureAwait(false);
    }

    private async Task<Sinal> CalcularIsoladoAsync(
        ICalculadorDeSinal calculador, Sinistro sinistro, CancellationToken ct)
    {
        try
        {
            return await calculador.CalcularAsync(sinistro, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new Sinal(
                calculador.Nome, ValorSinal.Indisponivel, Origem: "coletor",
                Evidencia: new Dictionary<string, object?> { ["falha"] = ex.GetType().Name },
                Motivo: MotivoIndisponibilidade.FonteIndisponivel,
                CalculadoEm: _clock.GetUtcNow());
        }
    }
}
