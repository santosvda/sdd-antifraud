using Antifraude.Core.Dominio;
using Antifraude.Infra.Persistencia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Antifraude.Tests.Integracao;

[Collection(IntegrationCollection.Nome)]
public sealed class AuditoriaImutavelTests(IntegrationFixture fixture)
{
    private AntifraudeDbContext NovoContexto()
    {
        var options = new DbContextOptionsBuilder<AntifraudeDbContext>()
            .UseMySql(fixture.ConnectionString, ServerVersion.AutoDetect(fixture.ConnectionString))
            .Options;
        return new AntifraudeDbContext(options);
    }

    private async Task<AntifraudeDbContext> ContextoMigradoAsync()
    {
        var db = NovoContexto();
        await db.Database.MigrateAsync();
        return db;
    }

    private static RegistroAuditoria NovoRegistro() => new()
    {
        Id = Guid.NewGuid(),
        CaseId = Guid.NewGuid(),
        Sinais = [new Sinal("reuso_imagem", 1.0, "mock")],
        Score = 42,
        Faixa = Faixa.Medio,
        Rota = Rota.Normal,
        VersaoConfig = 1,
        VersaoProvider = "mock-v1",
        Causa = null,
        Ator = "worker",
        CarimbadoEm = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task UPDATE_em_registro_de_auditoria_e_bloqueado_pelo_trigger()
    {
        await using var db = await ContextoMigradoAsync();
        var registro = NovoRegistro();
        db.Auditoria.Add(registro);
        await db.SaveChangesAsync();

        // UPDATE direto via SQL — o trigger BEFORE UPDATE deve disparar erro (SQLSTATE 45000).
        var update = async () => await db.Database.ExecuteSqlRawAsync(
            "UPDATE auditoria SET score = 0 WHERE id = {0}", registro.Id.ToString());

        await update.Should().ThrowAsync<Exception>()
            .Where(e => e.Message.Contains("append-only") || e.Message.Contains("45000"));

        // A linha permanece inalterada.
        var persistido = await db.Auditoria.AsNoTracking().FirstAsync(a => a.Id == registro.Id);
        persistido.Score.Should().Be(42);
    }

    [Fact]
    public async Task DELETE_em_registro_de_auditoria_e_bloqueado_pelo_trigger()
    {
        await using var db = await ContextoMigradoAsync();
        var registro = NovoRegistro();
        db.Auditoria.Add(registro);
        await db.SaveChangesAsync();

        var delete = async () => await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM auditoria WHERE id = {0}", registro.Id.ToString());

        await delete.Should().ThrowAsync<Exception>()
            .Where(e => e.Message.Contains("append-only") || e.Message.Contains("45000"));

        // A linha permanece presente.
        (await db.Auditoria.AsNoTracking().AnyAsync(a => a.Id == registro.Id)).Should().BeTrue();
    }
}
