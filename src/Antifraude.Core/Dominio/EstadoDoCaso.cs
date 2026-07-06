namespace Antifraude.Core.Dominio;

/// <summary>
/// Estado do caso no fluxo antifraude. Nunca existe um estado que negue, aprove
/// ou bloqueie o sinistro — o pior caso é <see cref="PendenteRevisaoManual"/>.
/// </summary>
public enum EstadoDoCaso
{
    /// <summary>Caso roteado para uma fila humana (normal ou reforçada) com score calculado.</summary>
    RoteadoParaRevisao,

    /// <summary>
    /// Fail-open: sinal faltante/parcial ou queda do <c>IScoreProvider</c>.
    /// O caso nasce visível e vai para revisão humana; nada é bloqueado.
    /// </summary>
    PendenteRevisaoManual,
}
