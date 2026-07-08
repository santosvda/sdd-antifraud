namespace Antifraude.Core.Portas;

/// <summary>pHash (64 bits) de uma foto do sinistro atual.</summary>
/// <param name="FotoRef">Referência (ID/URL) da foto — nunca bytes de imagem.</param>
/// <param name="Phash">Hash perceptual de 64 bits.</param>
public sealed record HashFoto(string FotoRef, ulong Phash);

/// <summary>pHash histórico de um sinistro anterior, para comparação de reuso.</summary>
/// <param name="IdSinistro">Sinistro anterior dono do hash.</param>
/// <param name="Phash">Hash perceptual de 64 bits.</param>
public sealed record HashHistorico(string IdSinistro, ulong Phash);

/// <summary>
/// Porta da fonte "Repositório de Imagens": fornece os pHashes das fotos do sinistro
/// atual e o histórico de hashes para o sinal de reuso de imagem. Só hashes trafegam —
/// nunca a imagem bruta (minimização LGPD). Pode lançar (indisponibilidade/timeout);
/// o calculador captura e marca o sinal como indisponível.
/// </summary>
public interface IRepositorioDeImagens
{
    /// <summary>Proveniência dos hashes, carimbada na evidência (ex.: <c>phash-fake-v1</c>).</summary>
    string Origem { get; }

    /// <summary>Obtém o pHash de cada foto do sinistro atual.</summary>
    Task<IReadOnlyList<HashFoto>> ObterHashesAsync(
        IReadOnlyList<string> fotos, CancellationToken ct = default);

    /// <summary>
    /// Hashes de sinistros anteriores desde <paramref name="desde"/> (janela de 6 meses),
    /// SEMPRE excluindo <paramref name="idSinistroAtual"/> — o caso nunca colide consigo
    /// mesmo, inclusive em reprocessamento.
    /// </summary>
    Task<IReadOnlyList<HashHistorico>> ObterHistoricoAsync(
        string idSinistroAtual, DateTimeOffset desde, CancellationToken ct = default);

    /// <summary>
    /// Registra os hashes do sinistro atual para os próximos casos. Chamado APÓS o
    /// cálculo; MUST ser upsert idempotente por (sinistro, foto) — retentativas não
    /// duplicam.
    /// </summary>
    Task RegistrarHashesAsync(
        string idSinistro, IReadOnlyList<HashFoto> hashes, DateTimeOffset em, CancellationToken ct = default);
}
