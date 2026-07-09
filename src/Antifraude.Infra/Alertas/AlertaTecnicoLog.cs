using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;
using Microsoft.Extensions.Logging;

namespace Antifraude.Infra.Alertas;

/// <summary>
/// Adapter de <see cref="IAlertaTecnico"/> da fundação: emite um log estruturado nível
/// Critical, correlacionado pelo <c>caseId</c>, com o código da anomalia. É o gancho pronto
/// para plugar um canal real de plantão (ex.: PagerDuty) sem alterar o domínio.
/// </summary>
public sealed class AlertaTecnicoLog(ILogger<AlertaTecnicoLog> logger) : IAlertaTecnico
{
    public Task EmitirAsync(
        SeveridadeAlerta severidade,
        string codigo,
        Guid caseId,
        string? detalhe = null,
        CancellationToken ct = default)
    {
        logger.Log(
            NivelPara(severidade),
            "ALERTA_TECNICO severidade={Severidade} codigo={Codigo} caseId={CaseId} detalhe={Detalhe}",
            severidade, codigo, caseId, detalhe);
        return Task.CompletedTask;
    }

    private static LogLevel NivelPara(SeveridadeAlerta severidade) => severidade switch
    {
        SeveridadeAlerta.Alta => LogLevel.Critical,
        SeveridadeAlerta.Media => LogLevel.Error,
        _ => LogLevel.Warning,
    };
}
