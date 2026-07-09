using Antifraude.Core.Dominio;

namespace Antifraude.Core.Portas;

/// <summary>
/// Porta de alerta técnico, <b>distinta do canal de alerta operacional</b>. Emitida quando o
/// motor detecta uma anomalia (score fora de faixa, config inválida) — não para
/// indisponibilidade esperada (fail-open normal). Na fundação o adapter apenas registra um log
/// estruturado nível Critical; a mesma porta liga um canal real (plantão/PagerDuty) sem tocar
/// no domínio. Emitir alerta NUNCA deve quebrar o fail-open — o chamador captura falhas.
/// </summary>
public interface IAlertaTecnico
{
    /// <summary>
    /// Emite um alerta técnico correlacionado por <paramref name="caseId"/>.
    /// </summary>
    /// <param name="severidade">Severidade do alerta.</param>
    /// <param name="codigo">Código da anomalia (ex.: o nome do motivo).</param>
    /// <param name="caseId">Correlação com o caso/requisição de origem.</param>
    /// <param name="detalhe">Detalhe legível da anomalia, quando houver.</param>
    Task EmitirAsync(SeveridadeAlerta severidade, string codigo, Guid caseId, string? detalhe = null, CancellationToken ct = default);
}
