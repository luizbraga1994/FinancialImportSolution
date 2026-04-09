using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace FinancialImport.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddSystemSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ConfiguracaoSistema",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                Chave = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                Valor = table.Column<string>(type: "text", nullable: true),
                Categoria = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                Descricao = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                TipoDado = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "string"),
                Obrigatorio = table.Column<bool>(type: "tinyint(1)", nullable: false),
                AtualizadoEm = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                AtualizadoPor = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConfiguracaoSistema", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "UQ_ConfiguracaoSistema_Chave",
            table: "ConfiguracaoSistema",
            column: "Chave",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ConfiguracaoSistema_Categoria",
            table: "ConfiguracaoSistema",
            column: "Categoria");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ConfiguracaoSistema");
    }
}
