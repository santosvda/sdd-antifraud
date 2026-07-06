using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antifraude.Infra.Persistencia.Migrations;

/// <summary>
/// Imutabilidade da trilha de auditoria garantida no nível do banco: triggers
/// BEFORE UPDATE / BEFORE DELETE que fazem SIGNAL SQLSTATE (erro). Nem um bug de
/// aplicação nem acesso direto ao banco conseguem alterar/remover a trilha — é
/// demonstrável: um UPDATE/DELETE dispara erro. EF não modela triggers, então o SQL
/// é cru nesta migration dedicada.
/// </summary>
public partial class AuditoriaImutavel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            @"CREATE TRIGGER trg_auditoria_no_update
                  BEFORE UPDATE ON auditoria
                  FOR EACH ROW
                  BEGIN
                      SIGNAL SQLSTATE '45000'
                          SET MESSAGE_TEXT = 'auditoria e append-only: UPDATE bloqueado';
                  END;");

        migrationBuilder.Sql(
            @"CREATE TRIGGER trg_auditoria_no_delete
                  BEFORE DELETE ON auditoria
                  FOR EACH ROW
                  BEGIN
                      SIGNAL SQLSTATE '45000'
                          SET MESSAGE_TEXT = 'auditoria e append-only: DELETE bloqueado';
                  END;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_auditoria_no_update;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_auditoria_no_delete;");
    }
}
