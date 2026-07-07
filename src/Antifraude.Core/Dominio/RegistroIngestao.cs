namespace Antifraude.Core.Dominio;

/// <summary>Resultado da checagem de idempotência na ingestão.</summary>
public enum ResultadoIdempotencia
{
    /// <summary>Primeira vez que este idSinistro é visto na janela — segue o fluxo.</summary>
    PrimeiraVez,

    /// <summary>Já visto na janela de 24h — descartado sem reprocessar.</summary>
    DuplicadoDescartado,

    /// <summary>Store de dedup indisponível — processado mesmo assim (fail-open).</summary>
    ChecagemIndisponivel,
}

/// <summary>Destino do roteamento de um evento recebido na ingestão.</summary>
public enum DestinoRoteamento
{
    /// <summary>Fila normal de processamento (feature downstream).</summary>
    FilaProcessamento,

    /// <summary>Fila de erro técnico (evento não-processável ou escalado após retry).</summary>
    FilaErroTecnico,

    /// <summary>Não roteado — evento descartado por idempotência (duplicado).</summary>
    Descartado,
}

/// <summary>
/// Registro append-only da auditoria da própria ingestão (Feature 2.1). Carimba, por evento
/// recebido, a completude do payload, o resultado da idempotência e o destino do roteamento.
/// A imutabilidade é garantida no banco por trigger que bloqueia UPDATE/DELETE.
/// </summary>
public sealed class RegistroIngestao
{
    public Guid Id { get; init; }

    /// <summary>Correlaciona ao caso/requisição de origem.</summary>
    public Guid CaseId { get; init; }

    /// <summary>Identificador do sinistro; <c>null</c> quando ausente (evento não-processável).</summary>
    public string? IdSinistro { get; init; }

    public bool TemApolice { get; init; }

    public bool TemAparelho { get; init; }

    public bool TemFotos { get; init; }

    public bool TemMetadados { get; init; }

    /// <summary>True quando falta algum campo não-estrutural do payload mínimo.</summary>
    public bool PayloadParcial { get; init; }

    public ResultadoIdempotencia Idempotencia { get; init; }

    public DestinoRoteamento Destino { get; init; }

    public DateTimeOffset RecebidoEm { get; init; }
}
