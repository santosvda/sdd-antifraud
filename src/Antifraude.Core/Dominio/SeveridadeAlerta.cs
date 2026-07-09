namespace Antifraude.Core.Dominio;

/// <summary>Severidade de um alerta técnico do motor.</summary>
public enum SeveridadeAlerta
{
    Baixa,
    Media,

    /// <summary>Anomalia que exige atenção imediata (ex.: plantão) — bug/estado inválido no motor.</summary>
    Alta,
}
