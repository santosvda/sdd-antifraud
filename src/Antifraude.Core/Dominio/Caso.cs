namespace Antifraude.Core.Dominio;

/// <summary>
/// Caso antifraude persistido. Resultado do processamento de um <see cref="Sinistro"/>:
/// sempre roteado para uma fila humana. Não existe campo de "culpado/fraudador" nem
/// estado que negue/aprove/bloqueie — a saída é <c>score + faixa + rota</c>.
/// </summary>
public sealed class Caso
{
    /// <summary>Identificador de correlação, o mesmo desde a recepção na API.</summary>
    public Guid CaseId { get; init; }

    public EstadoDoCaso Estado { get; init; }

    public Faixa Faixa { get; init; }

    public Rota Rota { get; init; }

    /// <summary>Score em [0,100]. <c>null</c> quando indeterminado (fail-open).</summary>
    public int? Score { get; init; }

    /// <summary>Versão da <c>scoring_config</c> que originou o resultado ("veio da config vN").</summary>
    public int VersaoConfig { get; init; }

    /// <summary>Versão/sinalização do <c>IScoreProvider</c> (ex.: <c>mock-v1</c>).</summary>
    public string VersaoProvider { get; init; } = string.Empty;

    /// <summary>True quando os sinais chegaram faltantes/parciais.</summary>
    public bool DadosIncompletos { get; init; }

    /// <summary>True quando o score foi calculado sobre cobertura parcial (2 de 3 sinais, pesos renormalizados).</summary>
    public bool CoberturaParcial { get; init; }

    /// <summary>True quando o payload de ingestão veio incompleto (campos não-estruturais ausentes).</summary>
    public bool PayloadParcial { get; init; }

    /// <summary>Explicação textual da faixa (template determinístico). <c>null</c> quando não há classificação.</summary>
    public string? Explicacao { get; init; }

    /// <summary>Versão do template de explicação usada. <c>null</c> quando não há explicação.</summary>
    public string? VersaoTemplate { get; init; }

    /// <summary>Motivo tipado quando o caso não recebeu faixa (fail-open ou anomalia). <c>null</c> quando classificado.</summary>
    public MotivoSemClassificacao? Motivo { get; init; }

    public DateTimeOffset CriadoEm { get; init; }
}
