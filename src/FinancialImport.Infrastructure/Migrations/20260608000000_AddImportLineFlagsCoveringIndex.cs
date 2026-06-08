using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialImport.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddImportLineFlagsCoveringIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The Preview page was loading all 34 k ImportLine rows to compute
        // per-group IsExcluded / IsImported flags.  With only (ImportFileId)
        // indexed, MySQL was doing a full clustered-index scan and timing out
        // on large imports.
        //
        // A composite index on (ImportacaoArquivoId, HashChaveGrupo, Status)
        // lets MySQL answer the compact flags query via an index-only scan
        // because InnoDB implicitly appends the PK (Id) to every secondary
        // index, covering all four columns the query needs.
        migrationBuilder.CreateIndex(
            name: "IX_ImportacaoLinha_FileIdGroupStatus",
            table: "ImportacaoLinha",
            columns: new[] { "ImportacaoArquivoId", "HashChaveGrupo", "Status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ImportacaoLinha_FileIdGroupStatus",
            table: "ImportacaoLinha");
    }
}
