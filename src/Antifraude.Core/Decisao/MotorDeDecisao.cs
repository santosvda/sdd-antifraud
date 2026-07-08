using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;

namespace Antifraude.Core.Decisao;

/// <summary>
/// Pipeline de decisão: resolve a config ativa → obtém o score via <see cref="IScoreProvider"/>
/// → classifica faixa/rota → produz um caso sempre roteado para fila humana + auditoria.
///
/// Guardrails materializados aqui:
/// <list type="bullet">
///   <item>Nunca nega/aprova/bloqueia — a saída é só <c>score + faixa + rota</c>.</item>
///   <item>Human-in-the-loop — todo caso vai para uma fila humana (normal|reforçada).</item>
///   <item>Fail-open — sinal faltante/parcial ou queda do provider vira
///   <see cref="EstadoDoCaso.PendenteRevisaoManual"/>, a causa é auditada e nada é bloqueado.</item>
///   <item>Nunca lança para fora — erro de domínio é capturado e vira estado.</item>
/// </list>
/// </summary>
public sealed class MotorDeDecisao(
    IScoringConfigRepository configRepository,
    IScoreProvider scoreProvider,
    TimeProvider? timeProvider = null)
{
    private const string Ator = "worker";

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<ResultadoDecisao> AvaliarAsync(Sinistro sinistro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sinistro);

        ScoringConfig config;
        try
        {
            config = await configRepository.ObterAtivaAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Sem config ativa também é fail-open: o caso nasce visível para revisão.
            return FailOpen(sinistro, versaoConfig: 0, $"Falha ao resolver scoring_config ativa: {ex.Message}");
        }

        ResultadoScore resultado;
        try
        {
            resultado = await scoreProvider.CalcularScoreAsync(sinistro, config, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Provider indisponível/timeout: captura, audita a causa, não bloqueia.
            return FailOpen(sinistro, config.Versao, $"IScoreProvider indisponível: {ex.Message}");
        }

        var conformidade = NotaDeConformidade(resultado.AtributosProibidosFiltrados);

        // Cobertura insuficiente: não fabrica score, o caso nasce visível para revisão.
        if (resultado.Score is not int score)
        {
            return FailOpen(
                sinistro,
                config.Versao,
                Concatenar(resultado.MotivoNaoAvaliado ?? "Score não avaliado", conformidade),
                dadosIncompletos: true);
        }

        score = Math.Clamp(score, 0, 100);
        var faixa = Classificador.FaixaPara(score, config);

        return Montar(
            sinistro,
            EstadoDoCaso.RoteadoParaRevisao,
            faixa,
            Classificador.RotaPara(faixa),
            score,
            config.Versao,
            causa: conformidade,
            dadosIncompletos: false,
            coberturaParcial: resultado.CoberturaParcial);
    }

    /// <summary>Compõe a nota de evento de conformidade quando atributos proibidos foram filtrados.</summary>
    private static string? NotaDeConformidade(IReadOnlyList<string> proibidos) =>
        proibidos.Count == 0 ? null : $"Atributos proibidos filtrados: {string.Join(", ", proibidos)}";

    private static string Concatenar(string causa, string? nota) =>
        nota is null ? causa : $"{causa} | {nota}";

    private ResultadoDecisao FailOpen(Sinistro sinistro, int versaoConfig, string causa, bool dadosIncompletos = false) =>
        Montar(
            sinistro,
            EstadoDoCaso.PendenteRevisaoManual,
            Faixa.Indeterminado,
            Rota.Reforcada,
            score: null,
            versaoConfig,
            causa,
            dadosIncompletos,
            coberturaParcial: false);

    private ResultadoDecisao Montar(
        Sinistro sinistro,
        EstadoDoCaso estado,
        Faixa faixa,
        Rota rota,
        int? score,
        int versaoConfig,
        string? causa,
        bool dadosIncompletos,
        bool coberturaParcial)
    {
        var agora = _clock.GetUtcNow();

        var caso = new Caso
        {
            CaseId = sinistro.CaseId,
            Estado = estado,
            Faixa = faixa,
            Rota = rota,
            Score = score,
            VersaoConfig = versaoConfig,
            VersaoProvider = scoreProvider.Versao,
            DadosIncompletos = dadosIncompletos,
            PayloadParcial = sinistro.PayloadParcial,
            CoberturaParcial = coberturaParcial,
            CriadoEm = agora,
        };

        var auditoria = new RegistroAuditoria
        {
            Id = Guid.NewGuid(),
            CaseId = sinistro.CaseId,
            Sinais = sinistro.Sinais ?? [],
            Score = score,
            Faixa = faixa,
            Rota = rota,
            VersaoConfig = versaoConfig,
            VersaoProvider = scoreProvider.Versao,
            Causa = causa,
            Ator = Ator,
            PayloadParcial = sinistro.PayloadParcial,
            CoberturaParcial = coberturaParcial,
            CarimbadoEm = agora,
        };

        return new ResultadoDecisao(caso, auditoria);
    }
}
