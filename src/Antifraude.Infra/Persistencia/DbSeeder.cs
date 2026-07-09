using Antifraude.Core.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antifraude.Infra.Persistencia;

/// <summary>
/// Semeia a configuração de scoring versionada. A v1 é o placeholder histórico da fundação
/// (mantido inativo). A <b>v2</b> é a config governada da Feature 2.3 (pesos 50/30/20 sobre
/// <c>reuso_imagem</c>/<c>imei_serie_divergente</c>/<c>velocity</c>, limiares 30/71) e nasce <b>ativa</b>.
/// Idempotente: só age se a v2 ainda não existir. Toda mudança de peso/limiar é uma nova
/// versão — nunca reescreve casos anteriores (BR7).
/// Semeia também a base fake de <c>apolices</c> (fonte do sinal IMEI×série) com
/// exemplos que cobrem os 3 ramos: confere, diverge e não cadastrado (= apólice ausente).
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AntifraudeDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        await SeedScoringConfigAsync(db, logger, ct).ConfigureAwait(false);
        await SeedApolicesAsync(db, logger, ct).ConfigureAwait(false);
    }

    private static async Task SeedScoringConfigAsync(AntifraudeDbContext db, ILogger? logger, CancellationToken ct)
    {
        var versoes = await db.ScoringConfigs.Select(c => c.Versao).ToListAsync(ct).ConfigureAwait(false);
        if (versoes.Contains(2))
        {
            return;
        }

        var agora = DateTimeOffset.UtcNow;

        // Desativa qualquer versão ativa anterior (Ativa é init-only no domínio → via SQL).
        await db.Database.ExecuteSqlRawAsync("UPDATE scoring_config SET ativa = 0", ct).ConfigureAwait(false);

        // Preserva o histórico: se a tabela estava vazia, registra a v1 placeholder inativa.
        if (versoes.Count == 0)
        {
            db.ScoringConfigs.Add(new ScoringConfig
            {
                Versao = 1,
                Ativa = false,
                Pesos = new Dictionary<string, double>
                {
                    ["reuso_imagem"] = 30,
                    ["imei_serie_divergente"] = 25,
                    ["geolocalizacao_inconsistente"] = 20,
                },
                LimiarMedio = 30,
                LimiarAlto = 60,
                CriadaEm = agora,
            });
        }

        db.ScoringConfigs.Add(new ScoringConfig
        {
            Versao = 2,
            Ativa = true,
            Pesos = new Dictionary<string, double>
            {
                [SinaisConhecidos.ReusoImagem] = 50,
                [SinaisConhecidos.ImeiSerie] = 30,
                [SinaisConhecidos.Velocity] = 20,
            },
            LimiarMedio = 30,
            LimiarAlto = 71,
            CriadaEm = agora,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        logger?.LogInformation("scoring_config v2 semeada (ativa); versões anteriores desativadas.");
    }

    private static async Task SeedApolicesAsync(AntifraudeDbContext db, ILogger? logger, CancellationToken ct)
    {
        if (await db.Apolices.AnyAsync(ct).ConfigureAwait(false))
        {
            return;
        }

        db.Apolices.AddRange(
            // Ramo "confere": aparelho do exemplo canônico da demo.
            new ApoliceRegistro { Apolice = "AP-2026-0042", Imei = "356938035643809", NumeroSerie = "SN-XYZ-123" },
            // Ramo "diverge": qualquer sinistro desta apólice com outro IMEI ativa o sinal.
            new ApoliceRegistro { Apolice = "AP-2026-0001", Imei = "990000862471854", NumeroSerie = "SN-ABC-001" });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        logger?.LogInformation("apolices de exemplo semeadas (fonte fake da coleta de sinais).");
    }
}
