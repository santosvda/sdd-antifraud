namespace Antifraude.Core.Dominio;

/// <summary>
/// Fila humana de destino. Ambas são revisão humana — nenhuma nega/aprova/bloqueia.
/// </summary>
public enum Rota
{
    /// <summary>Fila humana padrão.</summary>
    Normal,

    /// <summary>Fila humana reforçada (risco alto ou fail-open) — ainda assim só revisão.</summary>
    Reforcada,
}
