using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antifraude.Infra.Persistencia.Migrations;

/// <inheritdoc />
public partial class IngestaoSinistroReal : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "payload_parcial",
            table: "casos",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "payload_parcial",
            table: "auditoria",
            type: "tinyint(1)",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "auditoria_ingestao",
            columns: table => new
            {
                id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                id_sinistro = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                tem_apolice = table.Column<bool>(type: "tinyint(1)", nullable: false),
                tem_aparelho = table.Column<bool>(type: "tinyint(1)", nullable: false),
                tem_fotos = table.Column<bool>(type: "tinyint(1)", nullable: false),
                tem_metadados = table.Column<bool>(type: "tinyint(1)", nullable: false),
                payload_parcial = table.Column<bool>(type: "tinyint(1)", nullable: false),
                idempotencia = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                destino = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                recebido_em = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_auditoria_ingestao", x => x.id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "sinistros_processados",
            columns: table => new
            {
                id_sinistro = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                primeira_vez_em = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sinistros_processados", x => x.id_sinistro);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "ix_auditoria_ingestao_case_id",
            table: "auditoria_ingestao",
            column: "case_id");

        migrationBuilder.CreateIndex(
            name: "ix_auditoria_ingestao_id_sinistro",
            table: "auditoria_ingestao",
            column: "id_sinistro");

        migrationBuilder.CreateIndex(
            name: "ix_sinistros_processados_primeira_vez_em",
            table: "sinistros_processados",
            column: "primeira_vez_em");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "auditoria_ingestao");

        migrationBuilder.DropTable(
            name: "sinistros_processados");

        migrationBuilder.DropColumn(
            name: "payload_parcial",
            table: "casos");

        migrationBuilder.DropColumn(
            name: "payload_parcial",
            table: "auditoria");
    }
}
