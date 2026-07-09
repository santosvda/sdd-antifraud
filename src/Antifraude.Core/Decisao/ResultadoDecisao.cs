using Antifraude.Core.Dominio;

namespace Antifraude.Core.Decisao;

/// <summary>
/// Saída do <see cref="MotorDeDecisao"/>: o caso roteado e o registro de auditoria
/// correspondente, ambos correlacionados pelo mesmo <c>caseId</c>. O Worker apenas
/// persiste os dois. A explicação textual (<see cref="Caso.Explicacao"/>), a versão de
/// template (<see cref="Caso.VersaoTemplate"/>) e o motivo tipado
/// (<see cref="Caso.Motivo"/>) viajam dentro do <see cref="Caso"/> e do
/// <see cref="RegistroAuditoria"/> — não são duplicados aqui.
/// </summary>
public sealed record ResultadoDecisao(Caso Caso, RegistroAuditoria Auditoria);
