using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antifraude.Infra.Persistencia.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "auditoria",
            columns: table => new
            {
                id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                sinais_json = table.Column<string>(type: "json", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                score = table.Column<int>(type: "int", nullable: true),
                faixa = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                rota = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                versao_config = table.Column<int>(type: "int", nullable: false),
                versao_provider = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                causa = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ator = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                carimbado_em = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_auditoria", x => x.id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "casos",
            columns: table => new
            {
                case_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                estado = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                faixa = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                rota = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                score = table.Column<int>(type: "int", nullable: true),
                versao_config = table.Column<int>(type: "int", nullable: false),
                versao_provider = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                dados_incompletos = table.Column<bool>(type: "tinyint(1)", nullable: false),
                criado_em = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_casos", x => x.case_id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "scoring_config",
            columns: table => new
            {
                versao = table.Column<int>(type: "int", nullable: false),
                ativa = table.Column<bool>(type: "tinyint(1)", nullable: false),
                pesos_json = table.Column<string>(type: "json", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                limiar_medio = table.Column<int>(type: "int", nullable: false),
                limiar_alto = table.Column<int>(type: "int", nullable: false),
                criada_em = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_scoring_config", x => x.versao);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(
            name: "ix_auditoria_case_id",
            table: "auditoria",
            column: "case_id");

        migrationBuilder.CreateIndex(
            name: "ix_scoring_config_ativa",
            table: "scoring_config",
            column: "ativa");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "auditoria");

        migrationBuilder.DropTable(
            name: "casos");

        migrationBuilder.DropTable(
            name: "scoring_config");
    }
}
