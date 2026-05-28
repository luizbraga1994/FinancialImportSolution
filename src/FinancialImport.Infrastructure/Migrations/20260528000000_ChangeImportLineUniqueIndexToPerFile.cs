using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialImport.Infrastructure.Migrations;

/// <inheritdoc />
public partial class ChangeImportLineUniqueIndexToPerFile : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Replace the global (CompanyDb, HashChaveNegocio) unique constraint with a
        // per-file (ImportacaoArquivoId, HashChaveNegocio) unique constraint.
        // The old constraint prevented Duplicated-status lines from being inserted when
        // the same business key already existed in any other import file, making the
        // preview page show 0 groups for files where all lines were duplicates.
        migrationBuilder.DropIndex(
            name: "IX_ImportacaoLinha_CompanyDb_HashChaveNegocio",
            table: "ImportacaoLinha");

        migrationBuilder.CreateIndex(
            name: "IX_ImportacaoLinha_ImportFileId_HashChaveNegocio",
            table: "ImportacaoLinha",
            columns: new[] { "ImportacaoArquivoId", "HashChaveNegocio" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ImportacaoLinha_ImportFileId_HashChaveNegocio",
            table: "ImportacaoLinha");

        migrationBuilder.CreateIndex(
            name: "IX_ImportacaoLinha_CompanyDb_HashChaveNegocio",
            table: "ImportacaoLinha",
            columns: new[] { "CompanyDb", "HashChaveNegocio" },
            unique: true);
    }
}
