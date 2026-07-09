using Antifraude.Core.Dominio;

namespace Antifraude.Core.Classificacao;

/// <summary>
/// Fonte única, em código e versionada, dos textos determinísticos da Feature 2.4:
/// nome legível da faixa e o <b>rótulo canônico</b> não-acusatório de cada
/// <see cref="MotivoSemClassificacao"/>. Revisado por compliance via PR/diff. A versão
/// (<see cref="Versao"/>) é carimbada no caso e na auditoria ao lado da versão de limiares.
/// </summary>
public static class TemplateExplicacao
{
    /// <summary>Versão do conjunto de templates/rótulos. Muda a cada revisão de texto.</summary>
    public const string Versao = "tmpl-v1";

    /// <summary>Nome legível da faixa para uso na explicação.</summary>
    public static string FaixaTexto(Faixa faixa) => faixa switch
    {
        Faixa.Baixo => "baixa",
        Faixa.Medio => "média",
        Faixa.Alto => "alta",
        _ => throw new ArgumentOutOfRangeException(
            nameof(faixa), faixa, "Faixa indeterminada não tem explicação de faixa (ver rótulo canônico)."),
    };

    /// <summary>
    /// Rótulo canônico curto e não-acusatório da "marca" de sem-classificação, derivado
    /// exclusivamente do motivo tipado — nunca afirma fraude, nunca inventa faixa.
    /// </summary>
    public static string RotuloCanonico(MotivoSemClassificacao motivo) => motivo switch
    {
        MotivoSemClassificacao.SinalAusente =>
            "Não avaliado por IA — sinais ausentes; encaminhado para revisão manual.",
        MotivoSemClassificacao.ProviderIndisponivel =>
            "Não avaliado por IA — serviço de score indisponível; encaminhado para revisão manual.",
        MotivoSemClassificacao.ConfigIndisponivel =>
            "Não avaliado — configuração de risco indisponível; encaminhado para revisão manual.",
        MotivoSemClassificacao.ConfigCorrompida =>
            "Não avaliado — configuração de risco inválida; encaminhado para revisão manual.",
        MotivoSemClassificacao.ScoreForaDeFaixa =>
            "Não avaliado — inconsistência técnica no cálculo do score; encaminhado para revisão manual.",
        _ => "Não avaliado — encaminhado para revisão manual.",
    };
}
