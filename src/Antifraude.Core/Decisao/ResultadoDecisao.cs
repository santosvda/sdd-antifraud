using Antifraude.Core.Dominio;

namespace Antifraude.Core.Decisao;

/// <summary>
/// Saída do <see cref="MotorDeDecisao"/>: o caso roteado e o registro de auditoria
/// correspondente, ambos correlacionados pelo mesmo <c>caseId</c>. O Worker apenas
/// persiste os dois.
/// </summary>
public sealed record ResultadoDecisao(Caso Caso, RegistroAuditoria Auditoria);
