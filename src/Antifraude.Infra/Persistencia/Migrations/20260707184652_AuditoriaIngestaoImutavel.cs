using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antifraude.Infra.Persistencia.Migrations;

/// <summary>
/// Imutabilidade da trilha de auditoria da ingestão, na mesma disciplina append-only da
/// <c>auditoria</c>: triggers BEFORE UPDATE / BEFORE DELETE que fazem SIGNAL SQLSTATE (erro).
/// EF não modela triggers, então o SQL é cru nesta migration dedicada.
/// </summary>
public partial class AuditoriaIngestaoImutavel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            @"CREATE TRIGGER trg_auditoria_ingestao_no_update
                      BEFORE UPDATE ON auditoria_ingestao
                      FOR EACH ROW
                      BEGIN
                          SIGNAL SQLSTATE '45000'
                              SET MESSAGE_TEXT = 'auditoria_ingestao e append-only: UPDATE bloqueado';
                      END;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER trg_auditoria_ingestao_no_delete
                      BEFORE DELETE ON auditoria_ingestao
                      FOR EACH ROW
                      BEGIN
                          SIGNAL SQLSTATE '45000'
                              SET MESSAGE_TEXT = 'auditoria_ingestao e append-only: DELETE bloqueado';
                      END;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_auditoria_ingestao_no_update;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_auditoria_ingestao_no_delete;");
    }
}
