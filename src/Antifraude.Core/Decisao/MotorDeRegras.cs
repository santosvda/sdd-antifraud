using Antifraude.Core.Dominio;
using Antifraude.Core.Portas;

namespace Antifraude.Core.Decisao;

/// <summary>
/// Motor de score determinístico (Feature 2.3) — implementação real de <see cref="IScoreProvider"/>.
/// 100% puro: função de <c>(sinais, config)</c>, sem I/O, reprodutível (RF10). Score = soma dos
/// pesos dos sinais booleanos verdadeiros. Renormaliza sobre 2 de 3 sinais (piso de cobertura);
/// com 0 ou 1 sinal presente não avalia (fail-open a cargo do consumidor). Filtra atributos
/// proibidos antes do cálculo.
/// </summary>
public sealed class MotorDeRegras : IScoreProvider
{
    /// <summary>Piso mínimo de sinais presentes para calcular (abaixo disso: não avaliado).</summary>
    private const int PisoDeCobertura = 2;

    public string Versao => "regras-v1";

    public Task<ResultadoScore> CalcularScoreAsync(Sinistro sinistro, ScoringConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sinistro);
        ArgumentNullException.ThrowIfNull(config);

        var filtro = FiltroAtributosProibidos.Filtrar(sinistro.Sinais);
        var proibidos = filtro.ProibidosDetectados;

        // Estado explícito por sinal esperado: presente (com valor) ou ausente.
        var presentes = filtro.Permitidos
            .Where(s => SinaisConhecidos.Esperados.Contains(s.Nome))
            .GroupBy(s => s.Nome)
            .ToDictionary(g => g.Key, g => g.First());

        var nomesPresentes = SinaisConhecidos.Esperados.Where(presentes.ContainsKey).ToList();
        var nomesAusentes = SinaisConhecidos.Esperados.Where(n => !presentes.ContainsKey(n)).ToList();

        // Piso de cobertura: 0 ou 1 sinal → não avaliado, sem fabricar score.
        if (nomesPresentes.Count < PisoDeCobertura)
        {
            var motivo = $"Cobertura abaixo do piso: {nomesPresentes.Count} de {SinaisConhecidos.Esperados.Count} sinais presentes.";
            return Task.FromResult(new ResultadoScore(
                Score: null,
                CoberturaParcial: false,
                SinaisUsados: nomesPresentes,
                SinaisAusentes: nomesAusentes,
                MotivoNaoAvaliado: motivo,
                AtributosProibidosFiltrados: proibidos));
        }

        var coberturaParcial = nomesPresentes.Count < SinaisConhecidos.Esperados.Count;

        // Renormaliza os pesos dos presentes para somar 100 só quando parcial; cobertura cheia usa peso bruto.
        double somaPesosPresentes = nomesPresentes.Sum(n => config.Pesos.GetValueOrDefault(n, 0));
        double fator = coberturaParcial && somaPesosPresentes > 0 ? 100d / somaPesosPresentes : 1d;

        // Soma dos pesos (renormalizados) dos sinais presentes E verdadeiros.
        double bruto = nomesPresentes
            .Where(n => presentes[n].Valor != 0)
            .Sum(n => config.Pesos.GetValueOrDefault(n, 0) * fator);

        var score = (int)Math.Clamp(Math.Round(bruto, MidpointRounding.AwayFromZero), 0, 100);

        return Task.FromResult(new ResultadoScore(
            Score: score,
            CoberturaParcial: coberturaParcial,
            SinaisUsados: nomesPresentes,
            SinaisAusentes: nomesAusentes,
            MotivoNaoAvaliado: null,
            AtributosProibidosFiltrados: proibidos));
    }
}
