using Antifraude.Core.Dominio;

namespace Antifraude.Core.Decisao;

/// <summary>
/// Guardrail de não-discriminação materializado. Filtra, antes do cálculo, qualquer sinal
/// cujo nome corresponda a atributo sensível proibido (raça/cor, gênero, orientação sexual,
/// religião, deficiência, idade). Reforça o whitelist fechado dos 3 sinais válidos: nada
/// que não seja um sinal conhecido chega ao motor. 100% puro — sem dependência de infra.
/// </summary>
public static class FiltroAtributosProibidos
{
    /// <summary>Blocklist nomeada (PRD §6). Comparação case-insensitive por nome de sinal.</summary>
    private static readonly HashSet<string> Proibidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "raca", "raca_cor", "cor", "etnia",
        "genero", "sexo",
        "orientacao_sexual",
        "religiao",
        "deficiencia",
        "idade",
    };

    /// <summary>
    /// Separa os sinais permitidos (presentes no conjunto conhecido) dos atributos proibidos
    /// detectados. Sinais desconhecidos que não são proibidos são simplesmente descartados
    /// (não pontuam), sem gerar evento de conformidade.
    /// </summary>
    public static ResultadoFiltro Filtrar(IReadOnlyList<Sinal>? sinais)
    {
        var permitidos = new List<Sinal>();
        var proibidosDetectados = new List<string>();

        foreach (var sinal in sinais ?? [])
        {
            if (Proibidos.Contains(sinal.Nome))
            {
                proibidosDetectados.Add(sinal.Nome);
                continue;
            }

            if (SinaisConhecidos.Esperados.Contains(sinal.Nome))
            {
                permitidos.Add(sinal);
            }
            // Nome desconhecido e não-proibido: descartado silenciosamente (whitelist nega por padrão).
        }

        return new ResultadoFiltro(permitidos, proibidosDetectados);
    }
}

/// <summary>Saída do <see cref="FiltroAtributosProibidos"/>.</summary>
/// <param name="Permitidos">Sinais conhecidos que seguem para o cálculo.</param>
/// <param name="ProibidosDetectados">Nomes de atributos proibidos filtrados (evento de conformidade).</param>
public sealed record ResultadoFiltro(
    IReadOnlyList<Sinal> Permitidos,
    IReadOnlyList<string> ProibidosDetectados);
