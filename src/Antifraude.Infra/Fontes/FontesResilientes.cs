using Antifraude.Core.Portas;

namespace Antifraude.Infra.Fontes;

/// <summary>
/// Decorators de resiliência: cada porta de fonte de dados envolvida pelo
/// <see cref="CircuitoDaFonte"/> correspondente (timeout + circuit breaker independentes
/// por fonte). Quando as fontes virarem integrações HTTP reais, a troca acontece aqui —
/// as portas e os calculadores do Core não mudam.
/// </summary>
public sealed class RepositorioDeImagensResiliente(
    IRepositorioDeImagens interno, CircuitoDaFonte circuito) : IRepositorioDeImagens
{
    public string Origem => interno.Origem;

    public Task<IReadOnlyList<HashFoto>> ObterHashesAsync(
        IReadOnlyList<string> fotos, CancellationToken ct = default) =>
        circuito.ExecutarAsync(nameof(ObterHashesAsync), token => interno.ObterHashesAsync(fotos, token), ct);

    public Task<IReadOnlyList<HashHistorico>> ObterHistoricoAsync(
        string idSinistroAtual, DateTimeOffset desde, CancellationToken ct = default) =>
        circuito.ExecutarAsync(nameof(ObterHistoricoAsync), token => interno.ObterHistoricoAsync(idSinistroAtual, desde, token), ct);

    public Task RegistrarHashesAsync(
        string idSinistro, IReadOnlyList<HashFoto> hashes, DateTimeOffset em, CancellationToken ct = default) =>
        circuito.ExecutarAsync(nameof(RegistrarHashesAsync), token => interno.RegistrarHashesAsync(idSinistro, hashes, em, token), ct);
}

public sealed class BaseDeApolicesResiliente(
    IBaseDeApolices interno, CircuitoDaFonte circuito) : IBaseDeApolices
{
    public Task<AparelhoCadastrado?> ObterAparelhoCadastradoAsync(string apolice, CancellationToken ct = default) =>
        circuito.ExecutarAsync(nameof(ObterAparelhoCadastradoAsync), token => interno.ObterAparelhoCadastradoAsync(apolice, token), ct);
}

public sealed class HistoricoDeSinistrosResiliente(
    IHistoricoDeSinistros interno, CircuitoDaFonte circuito) : IHistoricoDeSinistros
{
    public Task<ContagemHistorico> ContarAsync(
        string idSinistroAtual, string? idCliente, string? imei, DateTimeOffset desde, CancellationToken ct = default) =>
        circuito.ExecutarAsync(nameof(ContarAsync), token => interno.ContarAsync(idSinistroAtual, idCliente, imei, desde, token), ct);

    public Task RegistrarAsync(
        string idSinistro, string? idCliente, string? imei, DateTimeOffset abertoEm, CancellationToken ct = default) =>
        circuito.ExecutarAsync(nameof(RegistrarAsync), token => interno.RegistrarAsync(idSinistro, idCliente, imei, abertoEm, token), ct);
}
