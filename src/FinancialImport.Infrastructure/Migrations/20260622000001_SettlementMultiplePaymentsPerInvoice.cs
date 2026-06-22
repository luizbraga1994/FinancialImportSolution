using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialImport.Infrastructure.Migrations;

/// <summary>
/// A single outgoing note may be settled by more than one Incoming Payment
/// (e.g. part in cash, part by transfer). The per-note uniqueness on the
/// business key is therefore wrong: uniqueness must be per-line (group key,
/// which carries the row ordinal). This swaps the unique index from the
/// business key to the group key.
/// </summary>
public partial class SettlementMultiplePaymentsPerInvoice : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BaixaLinha_FileId_HashChaveNegocio",
            table: "BaixaLinha");

        migrationBuilder.DropIndex(
            name: "IX_BaixaLinha_Grupo",
            table: "BaixaLinha");

        // Business key becomes a plain lookup index (cross-file dedup).
        migrationBuilder.CreateIndex(
            name: "IX_BaixaLinha_FileId_HashChaveNegocio",
            table: "BaixaLinha",
            columns: new[] { "BaixaArquivoId", "HashChaveNegocio" });

        // Group key (with row ordinal) becomes the per-line unique index.
        migrationBuilder.CreateIndex(
            name: "IX_BaixaLinha_Grupo",
            table: "BaixaLinha",
            columns: new[] { "BaixaArquivoId", "HashChaveGrupo" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BaixaLinha_Grupo",
            table: "BaixaLinha");

        migrationBuilder.DropIndex(
            name: "IX_BaixaLinha_FileId_HashChaveNegocio",
            table: "BaixaLinha");

        migrationBuilder.CreateIndex(
            name: "IX_BaixaLinha_Grupo",
            table: "BaixaLinha",
            columns: new[] { "BaixaArquivoId", "HashChaveGrupo" });

        migrationBuilder.CreateIndex(
            name: "IX_BaixaLinha_FileId_HashChaveNegocio",
            table: "BaixaLinha",
            columns: new[] { "BaixaArquivoId", "HashChaveNegocio" },
            unique: true);
    }
}
