namespace Antifraude.Core.Coleta;

/// <summary>
/// Mascaramento de identificadores técnicos (IMEI/série) para evidências e logs —
/// nunca in-the-clear além do necessário (LGPD, seção 12 do PRD).
/// </summary>
public static class MascaraLgpd
{
    /// <summary>Mantém só os últimos 4 caracteres (ex.: <c>…3809</c>).</summary>
    public static string Mascarar(string? valor) =>
        string.IsNullOrEmpty(valor) ? string.Empty
        : valor.Length <= 4 ? $"…{valor}"
        : $"…{valor[^4..]}";
}
