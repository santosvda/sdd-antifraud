using Antifraude.Core.Dominio;

namespace Antifraude.Core.Classificacao;

/// <summary>
/// Gera a explicação textual da faixa por <b>template determinístico</b> (Feature 2.4):
/// mesma entrada + mesma versão de template → mesma frase. Nomeia os sinais ativados
/// (<c>Valor &gt; 0</c>) em linguagem de indício, menciona cobertura parcial quando houver, e
/// nunca afirma fraude como fato consumado. É lógica pura do Core — sem infra.
/// </summary>
public static class GeradorDeExplicacao
{
    /// <summary>
    /// Monta a explicação para um caso classificado.
    /// </summary>
    /// <param name="score">Score em [0,100].</param>
    /// <param name="faixa">Faixa atribuída (não pode ser <see cref="Faixa.Indeterminado"/>).</param>
    /// <param name="sinais">Sinais do caso; apenas os ativados (<c>Valor &gt; 0</c>) são nomeados.</param>
    /// <param name="coberturaParcial">True quando o score foi calculado com pesos renormalizados por ausência de sinal.</param>
    public static string Gerar(int score, Faixa faixa, IReadOnlyList<Sinal> sinais, bool coberturaParcial)
    {
        ArgumentNullException.ThrowIfNull(sinais);

        var faixaTexto = TemplateExplicacao.FaixaTexto(faixa);

        var ativados = sinais
            .Where(s => s.Valor > 0)
            .Select(s => NomesDeSinais.Exibicao(s.Nome))
            .ToList();

        var corpo = ativados.Count == 0
            ? "Não foram destacados indícios específicos neste sinistro."
            : $"Este caso apresenta indícios de {Juntar(ativados)}.";

        var cobertura = coberturaParcial
            ? " O cálculo teve cobertura parcial (um ou mais sinais ausentes) — considerados apenas os indícios avaliados."
            : string.Empty;

        return $"Score de risco: {score}/100 — faixa {faixaTexto}. {corpo}{cobertura}";
    }

    /// <summary>Junta os nomes de forma determinística: "a", "a e b", "a, b e c".</summary>
    private static string Juntar(IReadOnlyList<string> itens) =>
        itens.Count == 1
            ? itens[0]
            : $"{string.Join(", ", itens.Take(itens.Count - 1))} e {itens[^1]}";
}
