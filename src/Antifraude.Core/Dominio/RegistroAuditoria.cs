namespace Antifraude.Core.Dominio;

/// <summary>
/// Registro append-only da trilha de auditoria. Carimba a decisão completa por caso:
/// sinais+origem, score, faixa, versão da config, versão do provider, rota, timestamp e ator.
/// A imutabilidade é garantida no banco por trigger que bloqueia UPDATE/DELETE.
/// </summary>
public sealed class RegistroAuditoria
{
    public Guid Id { get; init; }

    /// <summary>Correlaciona o registro ao caso e à requisição de origem.</summary>
    public Guid CaseId { get; init; }

    /// <summary>Sinais recebidos e sua origem. O formato de armazenamento (JSON) é decidido
    /// pelo adapter de persistência, não pelo domínio.</summary>
    public IReadOnlyList<Sinal> Sinais { get; init; } = [];

    /// <summary>Score calculado; <c>null</c> quando o provider falhou/sinal faltou.</summary>
    public int? Score { get; init; }

    public Faixa Faixa { get; init; }

    public Rota Rota { get; init; }

    public int VersaoConfig { get; init; }

    /// <summary>Versão/sinalização do provider — carimba explicitamente que veio de um mock.</summary>
    public string VersaoProvider { get; init; } = string.Empty;

    /// <summary>Causa registrada quando há fail-open (falha do provider ou sinal ausente).</summary>
    public string? Causa { get; init; }

    /// <summary>Explicação textual da faixa gerada por template. <c>null</c> quando não há classificação.</summary>
    public string? Explicacao { get; init; }

    /// <summary>Versão do template de explicação usada. <c>null</c> quando não há explicação.</summary>
    public string? VersaoTemplate { get; init; }

    /// <summary>Motivo tipado de sem-classificação (fail-open ou anomalia). <c>null</c> quando classificado.</summary>
    public MotivoSemClassificacao? Motivo { get; init; }

    /// <summary>Ator responsável pela decisão (ex.: <c>worker</c>).</summary>
    public string Ator { get; init; } = string.Empty;

    /// <summary>True quando o payload de ingestão veio incompleto (herdado do sinistro).</summary>
    public bool PayloadParcial { get; init; }

    /// <summary>True quando o score foi calculado sobre cobertura parcial (2 de 3 sinais, renormalizados).</summary>
    public bool CoberturaParcial { get; init; }

    public DateTimeOffset CarimbadoEm { get; init; }
}
