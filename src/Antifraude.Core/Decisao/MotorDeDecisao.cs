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

        // Nenhum sinal calculado (vazio ou todos indisponíveis) = "não avaliado":
        // não assume score baixo nem alto por omissão. Indisponibilidade parcial
        // segue para o score com os sinais disponíveis, marcada como dados incompletos.
        if (sinistro.SinaisIncompletos)
        {
            return FailOpen(sinistro, config.Versao, "Sinais faltantes ou indisponíveis", dadosIncompletos: true);
        }

        int score;
        try
        {
            score = await scoreProvider.CalcularScoreAsync(sinistro, config, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Provider indisponível/timeout: captura, audita a causa, não bloqueia.
            return FailOpen(sinistro, config.Versao, $"IScoreProvider indisponível: {ex.Message}");
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
            causa: null,
            dadosIncompletos: sinistro.AlgumSinalIndisponivel);
    }

    private ResultadoDecisao FailOpen(Sinistro sinistro, int versaoConfig, string causa, bool dadosIncompletos = false) =>
        Montar(
            sinistro,
            EstadoDoCaso.PendenteRevisaoManual,
            Faixa.Indeterminado,
            Rota.Reforcada,
            score: null,
            versaoConfig,
            causa,
            dadosIncompletos);

    private ResultadoDecisao Montar(
        Sinistro sinistro,
        EstadoDoCaso estado,
        Faixa faixa,
        Rota rota,
        int? score,
        int versaoConfig,
        string? causa,
        bool dadosIncompletos)
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
            CarimbadoEm = agora,
        };

        return new ResultadoDecisao(caso, auditoria);
    }
}
