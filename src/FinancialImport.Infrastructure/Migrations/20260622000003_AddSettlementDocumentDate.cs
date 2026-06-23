using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialImport.Infrastructure.Migrations;

/// <summary>
/// Adds the document date (DataDocumento) to settlement lines. It is matched
/// against OINV.TaxDate when resolving the open invoice and is part of the
/// fiscal note identity.
/// </summary>
public partial class AddSettlementDocumentDate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DataDocumento",
            table: "BaixaLinha",
            type: "datetime(6)",
            nullable: false,
            defaultValueSql: "'1900-01-01 00:00:00'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DataDocumento", table: "BaixaLinha");
    }
}
