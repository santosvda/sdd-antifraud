using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antifraude.Infra.Persistencia.Migrations;

/// <inheritdoc />
public partial class ClassificacaoRisco : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "explicacao",
            table: "casos",
            type: "varchar(2000)",
            maxLength: 2000,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "motivo",
            table: "casos",
            type: "varchar(40)",
            maxLength: 40,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "versao_template",
            table: "casos",
            type: "varchar(40)",
            maxLength: 40,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "explicacao",
            table: "auditoria",
            type: "varchar(2000)",
            maxLength: 2000,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "motivo",
            table: "auditoria",
            type: "varchar(40)",
            maxLength: 40,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.AddColumn<string>(
            name: "versao_template",
            table: "auditoria",
            type: "varchar(40)",
            maxLength: 40,
            nullable: true)
            .Annotation("MySql:CharSet", "utf8mb4");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "explicacao",
            table: "casos");

        migrationBuilder.DropColumn(
            name: "motivo",
            table: "casos");

        migrationBuilder.DropColumn(
            name: "versao_template",
            table: "casos");

        migrationBuilder.DropColumn(
            name: "explicacao",
            table: "auditoria");

        migrationBuilder.DropColumn(
            name: "motivo",
            table: "auditoria");

        migrationBuilder.DropColumn(
            name: "versao_template",
            table: "auditoria");
    }
}
