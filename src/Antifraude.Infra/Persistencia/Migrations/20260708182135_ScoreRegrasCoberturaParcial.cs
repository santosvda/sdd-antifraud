using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antifraude.Infra.Persistencia.Migrations;

/// <inheritdoc />
public partial class ScoreRegrasCoberturaParcial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "cobertura_parcial",
            table: "casos",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "cobertura_parcial",
            table: "auditoria",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "cobertura_parcial",
            table: "casos");

        migrationBuilder.DropColumn(
            name: "cobertura_parcial",
            table: "auditoria");
    }
}
