using Antifraude.Core.Dominio;

namespace Antifraude.Core.Decisao;

/// <summary>
/// Lógica pura de classificação: score → faixa (por limiar da config) → rota.
/// Sem dependência de infra — 100% testável em unit. Nenhuma saída aqui nega,
/// aprova ou bloqueia: o resultado é sempre uma rota de fila humana.
/// </summary>
public static class Classificador
{
    /// <summary>Mapeia o score para faixa usando os limiares da <see cref="ScoringConfig"/> ativa.</summary>
    public static Faixa FaixaPara(int score, ScoringConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (score >= config.LimiarAlto)
        {
            return Faixa.Alto;
        }

        if (score >= config.LimiarMedio)
        {
            return Faixa.Medio;
        }

        return Faixa.Baixo;
    }

    /// <summary>
    /// Mapeia a faixa para a fila humana. Alto e Indeterminado vão para a reforçada;
    /// as demais para a normal. Ambas são revisão humana — nada é bloqueado.
    /// </summary>
    public static Rota RotaPara(Faixa faixa) => faixa switch
    {
        Faixa.Alto or Faixa.Indeterminado => Rota.Reforcada,
        _ => Rota.Normal,
    };
}
