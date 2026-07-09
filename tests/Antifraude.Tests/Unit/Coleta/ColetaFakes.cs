using Antifraude.Core.Portas;

namespace Antifraude.Tests.Unit.Coleta;

/// <summary>Repositório de imagens em memória, com queda simulável e contagem de chamadas.</summary>
internal sealed class FakeRepositorioDeImagens : IRepositorioDeImagens
{
    public string Origem => "phash-fake-teste";

    public bool Lancar { get; set; }

    /// <summary>Hash devolvido por referência de foto (default: hash trivial da string).</summary>
    public Dictionary<string, ulong> HashPorFoto { get; } = [];

    public List<HashHistorico> Historico { get; } = [];

    public List<(string IdSinistro, IReadOnlyList<HashFoto> Hashes)> Registrados { get; } = [];

    public int Chamadas { get; private set; }

    public Task<IReadOnlyList<HashFoto>> ObterHashesAsync(IReadOnlyList<string> fotos, CancellationToken ct = default)
    {
        Chamadas++;
        if (Lancar)
        {
            throw new InvalidOperationException("fonte de imagens fora (simulado)");
        }

        IReadOnlyList<HashFoto> hashes =
            [.. fotos.Select(f => new HashFoto(f, HashPorFoto.TryGetValue(f, out var h) ? h : (ulong)f.GetHashCode()))];
        return Task.FromResult(hashes);
    }

    public Task<IReadOnlyList<HashHistorico>> ObterHistoricoAsync(
        string idSinistroAtual, DateTimeOffset desde, CancellationToken ct = default)
    {
        Chamadas++;
        if (Lancar)
        {
            throw new InvalidOperationException("fonte de imagens fora (simulado)");
        }

        IReadOnlyList<HashHistorico> resultado = [.. Historico.Where(h => h.IdSinistro != idSinistroAtual)];
        return Task.FromResult(resultado);
    }

    public Task RegistrarHashesAsync(
        string idSinistro, IReadOnlyList<HashFoto> hashes, DateTimeOffset em, CancellationToken ct = default)
    {
        Registrados.Add((idSinistro, hashes));
        return Task.CompletedTask;
    }
}

/// <summary>Base de apólices em memória: um aparelho cadastrado configurável por apólice.</summary>
internal sealed class FakeBaseDeApolices : IBaseDeApolices
{
    public bool Lancar { get; set; }

    public Dictionary<string, AparelhoCadastrado> PorApolice { get; } = [];

    public int Chamadas { get; private set; }

    public Task<AparelhoCadastrado?> ObterAparelhoCadastradoAsync(string apolice, CancellationToken ct = default)
    {
        Chamadas++;
        if (Lancar)
        {
            throw new InvalidOperationException("base de apólices fora (simulado)");
        }

        return Task.FromResult(PorApolice.TryGetValue(apolice, out var aparelho) ? aparelho : null);
    }
}

/// <summary>Histórico de sinistros em memória, com contagens fixas e captura da janela usada.</summary>
internal sealed class FakeHistoricoDeSinistros : IHistoricoDeSinistros
{
    public bool Lancar { get; set; }

    public ContagemHistorico Contagem { get; set; } = new(0, 0);

    public DateTimeOffset? DesdeRecebido { get; private set; }

    public List<(string IdSinistro, string? IdCliente, string? Imei, DateTimeOffset AbertoEm)> Registrados { get; } = [];

    public int Chamadas { get; private set; }

    public Task<ContagemHistorico> ContarAsync(
        string idSinistroAtual, string? idCliente, string? imei, DateTimeOffset desde, CancellationToken ct = default)
    {
        Chamadas++;
        if (Lancar)
        {
            throw new InvalidOperationException("histórico de sinistros fora (simulado)");
        }

        DesdeRecebido = desde;
        return Task.FromResult(Contagem);
    }

    public Task RegistrarAsync(
        string idSinistro, string? idCliente, string? imei, DateTimeOffset abertoEm, CancellationToken ct = default)
    {
        Registrados.Add((idSinistro, idCliente, imei, abertoEm));
        return Task.CompletedTask;
    }
}
