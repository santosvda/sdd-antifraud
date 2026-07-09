namespace Antifraude.Core.Dominio;

/// <summary>
/// Motivo tipado de um caso que não recebeu faixa. Distingue <b>indisponibilidade
/// esperada</b> (fail-open normal — não alerta) de <b>anomalia técnica</b> (indica bug/estado
/// inválido — dispara alerta técnico). O disparo do alerta deriva do motivo, nunca de parse
/// de texto livre. Ver <see cref="MotivoSemClassificacaoExtensions.EhAnomalia"/>.
/// </summary>
public enum MotivoSemClassificacao
{
    /// <summary>Indisponibilidade esperada: sinais faltantes/parciais.</summary>
    SinalAusente,

    /// <summary>Indisponibilidade esperada: <c>IScoreProvider</c> caiu/deu timeout.</summary>
    ProviderIndisponivel,

    /// <summary>Anomalia: a <c>scoring_config</c> ativa não pôde ser resolvida.</summary>
    ConfigIndisponivel,

    /// <summary>Anomalia: a configuração de limiares está inválida/corrompida.</summary>
    ConfigCorrompida,

    /// <summary>Anomalia: score recebido fora do intervalo [0,100] (erro upstream).</summary>
    ScoreForaDeFaixa,
}

/// <summary>Semântica de severidade dos motivos de sem-classificação.</summary>
public static class MotivoSemClassificacaoExtensions
{
    /// <summary>
    /// True quando o motivo indica anomalia técnica (bug/estado inválido) e, portanto,
    /// deve disparar alerta técnico de severidade alta. Indisponibilidade esperada
    /// (sinal ausente, provider caído) retorna false.
    /// </summary>
    public static bool EhAnomalia(this MotivoSemClassificacao motivo) => motivo switch
    {
        MotivoSemClassificacao.ConfigIndisponivel
            or MotivoSemClassificacao.ConfigCorrompida
            or MotivoSemClassificacao.ScoreForaDeFaixa => true,
        _ => false,
    };
}
