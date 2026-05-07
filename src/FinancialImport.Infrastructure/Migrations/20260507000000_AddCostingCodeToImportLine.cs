using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialImport.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddCostingCodeToImportLine : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CentroCusto",
            table: "ImportacaoLinha",
            type: "varchar(20)",
            maxLength: 20,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CentroCusto",
            table: "ImportacaoLinha");
    }
}
