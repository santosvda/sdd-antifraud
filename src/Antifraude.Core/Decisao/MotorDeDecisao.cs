using Antifraude.Core.Classificacao;
using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;

namespace Antifraude.Core.Decisao;

/// <summary>
/// Pipeline de decisão: resolve a config ativa → obtém o score via <see cref="IScoreProvider"/>
/// → classifica faixa/rota + gera explicação → produz um caso sempre roteado para fila humana
/// + auditoria.
///
/// Guardrails materializados aqui:
/// <list type="bullet">
///   <item>Nunca nega/aprova/bloqueia — a saída é só <c>score + faixa + rota</c>.</item>
///   <item>Human-in-the-loop — todo caso vai para uma fila humana (normal|reforçada).</item>
///   <item>Fail-open — sinal faltante/parcial ou queda do provider vira
///   <see cref="EstadoDoCaso.PendenteRevisaoManual"/> com <see cref="MotivoSemClassificacao"/>
///   tipado, a causa é auditada e nada é bloqueado.</item>
///   <item>Score fora de [0,100] NÃO é coagido: vira sem-classificação por anomalia
///   (<see cref="MotivoSemClassificacao.ScoreForaDeFaixa"/>) + alerta técnico severidade alta.</item>
///   <item>Nunca lança para fora — erro de domínio é capturado e vira estado.</item>
/// </list>
/// </summary>
public sealed class MotorDeDecisao(
    IScoringConfigRepository configRepository,
    IScoreProvider scoreProvider,
    TimeProvider? timeProvider = null,
    IAlertaTecnico? alertaTecnico = null)
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
            // Sem config ativa é anomalia (nunca operar sem limiares validados): fail-open + alerta.
            return await SemClassificacaoAsync(
                sinistro, versaoConfig: 0, MotivoSemClassificacao.ConfigIndisponivel,
                $"Falha ao resolver scoring_config ativa: {ex.Message}", ct).ConfigureAwait(false);
        }

        // Sinal faltante/parcial: não assume score baixo nem alto por omissão.
        if (sinistro.SinaisIncompletos)
        {
            return await SemClassificacaoAsync(
                sinistro, config.Versao, MotivoSemClassificacao.SinalAusente,
                "Sinais faltantes ou parciais", ct).ConfigureAwait(false);
        }

        int score;
        try
        {
            score = await scoreProvider.CalcularScoreAsync(sinistro, config, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Provider indisponível/timeout: captura, audita a causa, não bloqueia.
            return await SemClassificacaoAsync(
                sinistro, config.Versao, MotivoSemClassificacao.ProviderIndisponivel,
                $"IScoreProvider indisponível: {ex.Message}", ct).ConfigureAwait(false);
        }

        // Score fora de [0,100]: anomalia técnica — sem coagir o valor (nenhum clamp silencioso).
        if (Classificador.ForaDeFaixa(score))
        {
            return await SemClassificacaoAsync(
                sinistro, config.Versao, MotivoSemClassificacao.ScoreForaDeFaixa,
                $"Score fora do intervalo [0,100]: {score}", ct).ConfigureAwait(false);
        }

        var faixa = Classificador.FaixaPara(score, config);
        // Cobertura parcial é entrada própria da 2.4; até a 2.3 expor o dado, o seam passa false.
        var explicacao = GeradorDeExplicacao.Gerar(score, faixa, sinistro.Sinais ?? [], coberturaParcial: false);

        return Montar(sinistro, faixa, score, config.Versao, causa: null, motivo: null, explicacao);
    }

    private async Task<ResultadoDecisao> SemClassificacaoAsync(
        Sinistro sinistro,
        int versaoConfig,
        MotivoSemClassificacao motivo,
        string causa,
        CancellationToken ct)
    {
        var resultado = Montar(sinistro, Faixa.Indeterminado, score: null, versaoConfig, causa, motivo, explicacao: null);

        // Anomalia técnica dispara alerta severidade alta; indisponibilidade esperada não.
        // Emitir o alerta NUNCA quebra o fail-open.
        if (motivo.EhAnomalia() && alertaTecnico is not null)
        {
            try
            {
                await alertaTecnico
                    .EmitirAsync(SeveridadeAlerta.Alta, motivo.ToString(), sinistro.CaseId, causa, ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Falha ao alertar não pode derrubar o caso: o sinistro segue seu curso.
            }
        }

        return resultado;
    }

    // Estado, rota e "dados incompletos" são derivados de faixa/motivo — evita construir um
    // Caso internamente inconsistente e mantém a lista de parâmetros enxuta.
    private ResultadoDecisao Montar(
        Sinistro sinistro,
        Faixa faixa,
        int? score,
        int versaoConfig,
        string? causa,
        MotivoSemClassificacao? motivo,
        string? explicacao)
    {
        var agora = _clock.GetUtcNow();
        var estado = motivo is null ? EstadoDoCaso.RoteadoParaRevisao : EstadoDoCaso.PendenteRevisaoManual;
        var rota = Classificador.RotaPara(faixa);
        var dadosIncompletos = motivo == MotivoSemClassificacao.SinalAusente;
        var versaoTemplate = explicacao is null ? null : TemplateExplicacao.Versao;

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
            Explicacao = explicacao,
            VersaoTemplate = versaoTemplate,
            Motivo = motivo,
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
            Explicacao = explicacao,
            VersaoTemplate = versaoTemplate,
            Motivo = motivo,
            Ator = Ator,
            PayloadParcial = sinistro.PayloadParcial,
            CarimbadoEm = agora,
        };

        return new ResultadoDecisao(caso, auditoria);
    }
}
