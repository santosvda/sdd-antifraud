using Antifraude.Core.Dominio;
using Antifraude.Infra.Persistencia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Antifraude.Tests.Integracao;

[Collection(IntegrationCollection.Nome)]
public sealed class AuditoriaIngestaoImutavelTests(IntegrationFixture fixture)
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

    private static RegistroIngestao NovoRegistro() => new()
    {
        Id = Guid.NewGuid(),
        CaseId = Guid.NewGuid(),
        IdSinistro = $"SIN-{Guid.NewGuid():N}",
        TemApolice = true,
        TemAparelho = true,
        TemFotos = true,
        TemMetadados = true,
        PayloadParcial = false,
        Idempotencia = ResultadoIdempotencia.PrimeiraVez,
        Destino = DestinoRoteamento.FilaProcessamento,
        RecebidoEm = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task UPDATE_em_auditoria_ingestao_e_bloqueado_pelo_trigger()
    {
        await using var db = await ContextoMigradoAsync();
        var registro = NovoRegistro();
        db.AuditoriaIngestao.Add(registro);
        await db.SaveChangesAsync();

        var update = async () => await db.Database.ExecuteSqlRawAsync(
            "UPDATE auditoria_ingestao SET payload_parcial = 1 WHERE id = {0}", registro.Id.ToString());

        await update.Should().ThrowAsync<Exception>()
            .Where(e => e.Message.Contains("append-only") || e.Message.Contains("45000"));

        var persistido = await db.AuditoriaIngestao.AsNoTracking().FirstAsync(a => a.Id == registro.Id);
        persistido.PayloadParcial.Should().BeFalse();
    }

    [Fact]
    public async Task DELETE_em_auditoria_ingestao_e_bloqueado_pelo_trigger()
    {
        await using var db = await ContextoMigradoAsync();
        var registro = NovoRegistro();
        db.AuditoriaIngestao.Add(registro);
        await db.SaveChangesAsync();

        var delete = async () => await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM auditoria_ingestao WHERE id = {0}", registro.Id.ToString());

        await delete.Should().ThrowAsync<Exception>()
            .Where(e => e.Message.Contains("append-only") || e.Message.Contains("45000"));

        (await db.AuditoriaIngestao.AsNoTracking().AnyAsync(a => a.Id == registro.Id)).Should().BeTrue();
    }
}
