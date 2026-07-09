using Antifraude.Core.Portas;
using Antifraude.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Antifraude.Infra.Fontes;

/// <summary>
/// Fonte "Repositório de Imagens" — fake local persistido no MySQL. Sem acesso aos bytes
/// da imagem nesta fatia, o pHash é derivado DETERMINISTICAMENTE da referência da foto
/// (FNV-1a 64 bits) e a origem é carimbada <c>phash-fake-v1</c> em toda evidência: mesma
/// referência ⇒ mesmo hash ⇒ distância 0 ⇒ reuso detectável. A troca por pHash visual
/// real acontece só aqui, sem tocar o Core.
///
/// Usa <see cref="IDbContextFactory{TContext}"/> (contexto próprio por operação) porque
/// os calculadores rodam em paralelo e o DbContext não é thread-safe.
/// </summary>
public sealed class RepositorioDeImagensMySql(IDbContextFactory<AntifraudeDbContext> dbFactory)
    : IRepositorioDeImagens
{
    public string Origem => "phash-fake-v1";

    public Task<IReadOnlyList<HashFoto>> ObterHashesAsync(
        IReadOnlyList<string> fotos, CancellationToken ct = default)
    {
        IReadOnlyList<HashFoto> hashes = [.. fotos.Select(f => new HashFoto(f, PhashFake(f)))];
        return Task.FromResult(hashes);
    }

    public async Task<IReadOnlyList<HashHistorico>> ObterHistoricoAsync(
        string idSinistroAtual, DateTimeOffset desde, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Invariante: exclui o próprio sinistro — o caso nunca colide consigo mesmo,
        // inclusive quando a mensagem é reprocessada após um registro bem-sucedido.
        var linhas = await db.ImagemHashes.AsNoTracking()
            .Where(h => h.CriadoEm >= desde && h.IdSinistro != idSinistroAtual)
            .Select(h => new { h.IdSinistro, h.Phash })
            .ToListAsync(ct).ConfigureAwait(false);

        return [.. linhas.Select(l => new HashHistorico(l.IdSinistro, unchecked((ulong)l.Phash)))];
    }

    public async Task RegistrarHashesAsync(
        string idSinistro, IReadOnlyList<HashFoto> hashes, DateTimeOffset em, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existentes = await db.ImagemHashes.AsNoTracking()
            .Where(h => h.IdSinistro == idSinistro)
            .Select(h => h.FotoRef)
            .ToListAsync(ct).ConfigureAwait(false);

        var novos = hashes
            .Where(h => !existentes.Contains(h.FotoRef))
            .Select(h => new ImagemHashRegistro
            {
                IdSinistro = idSinistro,
                FotoRef = h.FotoRef,
                Phash = unchecked((long)h.Phash),
                CriadoEm = em,
            })
            .ToList();
        if (novos.Count == 0)
        {
            return;
        }

        db.ImagemHashes.AddRange(novos);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Corrida com outro processamento da mesma mensagem: o índice único
            // (sinistro, foto) garante a idempotência — duplicata é não-evento.
        }
    }

    /// <summary>FNV-1a 64 bits sobre a referência da foto — placeholder sinalizado do pHash.</summary>
    public static ulong PhashFake(string fotoRef)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong primo = 1099511628211UL;

        var hash = offset;
        foreach (var c in fotoRef.Trim())
        {
            hash ^= c;
            hash *= primo;
        }

        return hash;
    }
}
