namespace Antifraude.Core.Portas;

/// <summary>Contagens de sinistros anteriores na janela consultada.</summary>
/// <param name="PorCliente">Sinistros do mesmo cliente (0 quando cliente não informado).</param>
/// <param name="PorAparelho">Sinistros do mesmo IMEI (0 quando IMEI não informado).</param>
public sealed record ContagemHistorico(int PorCliente, int PorAparelho);

/// <summary>
/// Porta da fonte "Histórico de Sinistros": contagens por cliente/aparelho para o sinal
/// de velocity. Pode lançar (indisponibilidade/timeout); o calculador captura e marca o
/// sinal como indisponível.
/// </summary>
public interface IHistoricoDeSinistros
{
    /// <summary>
    /// Conta sinistros desde <paramref name="desde"/> (janela de 90 dias) por cliente e
    /// por IMEI, SEMPRE excluindo <paramref name="idSinistroAtual"/> — o caso nunca se
    /// conta, inclusive em reprocessamento.
    /// </summary>
    Task<ContagemHistorico> ContarAsync(
        string idSinistroAtual,
        string? idCliente,
        string? imei,
        DateTimeOffset desde,
        CancellationToken ct = default);

    /// <summary>
    /// Registra o sinistro atual no histórico para os próximos casos. Chamado APÓS o
    /// cálculo; MUST ser upsert idempotente por <paramref name="idSinistro"/> —
    /// retentativas não duplicam nem inflam contagens.
    /// </summary>
    Task RegistrarAsync(
        string idSinistro,
        string? idCliente,
        string? imei,
        DateTimeOffset abertoEm,
        CancellationToken ct = default);
}
