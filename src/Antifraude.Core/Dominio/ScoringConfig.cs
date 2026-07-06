namespace Antifraude.Core.Dominio;

/// <summary>
/// Configuração de scoring versionada. Vive no banco (tabela <c>scoring_config</c>) —
/// nunca hard-coded, nunca em env var. A versão ativa é resolvida no cálculo e
/// carimbada no caso e na auditoria.
/// </summary>
public sealed class ScoringConfig
{
    public int Versao { get; init; }

    public bool Ativa { get; init; }

    /// <summary>Peso de cada sinal por nome (ex.: <c>reuso_imagem → 30</c>).</summary>
    public IReadOnlyDictionary<string, double> Pesos { get; init; } =
        new Dictionary<string, double>();

    /// <summary>Score &gt;= este limiar entra na faixa Média.</summary>
    public int LimiarMedio { get; init; }

    /// <summary>Score &gt;= este limiar entra na faixa Alta.</summary>
    public int LimiarAlto { get; init; }

    public DateTimeOffset CriadaEm { get; init; }
}
