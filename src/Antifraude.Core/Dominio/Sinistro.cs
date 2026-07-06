namespace Antifraude.Core.Dominio;

/// <summary>
/// Sinistro recebido para análise. É a mensagem que trafega da API pela fila até o Worker.
/// O <see cref="CaseId"/> costura request → fila → worker → caso → auditoria.
/// </summary>
/// <param name="CaseId">Identificador de correlação gerado na borda (API).</param>
/// <param name="Sinais">Sinais coletados. Pode ser vazio/parcial — o motor lida via fail-open.</param>
public sealed record Sinistro(Guid CaseId, IReadOnlyList<Sinal> Sinais)
{
    /// <summary>True quando não há sinais suficientes para uma decisão confiável.</summary>
    public bool SinaisIncompletos => Sinais is null || Sinais.Count == 0;
}
