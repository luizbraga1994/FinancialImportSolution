using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialImport.Infrastructure.Migrations;

/// <inheritdoc />
public partial class ChangeDispatchUniqueIndexToPerFile : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The global (CompanyDb, HashChaveGrupo) unique index caused the
        // ImportProcessor to find any previous dispatch for the same company+group
        // across ALL import files and skip the SAP call entirely ("Already dispatched
        // (idempotent)"), marking lines as Imported without ever contacting SAP.
        //
        // Per-file idempotency (ImportacaoArquivoId, HashChaveGrupo) is the correct
        // scope: within a single import file a group is never sent twice, but
        // different import files — including force-reimports — always make fresh calls.
        migrationBuilder.DropIndex(
            name: "IX_LancamentoSapDispatch_CompanyDb_HashChaveGrupo",
            table: "LancamentoSapDispatch");

        migrationBuilder.CreateIndex(
            name: "IX_LancamentoSapDispatch_ImportFileId_GroupKeyHash",
            table: "LancamentoSapDispatch",
            columns: new[] { "ImportacaoArquivoId", "HashChaveGrupo" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_LancamentoSapDispatch_ImportFileId_GroupKeyHash",
            table: "LancamentoSapDispatch");

        migrationBuilder.CreateIndex(
            name: "IX_LancamentoSapDispatch_CompanyDb_HashChaveGrupo",
            table: "LancamentoSapDispatch",
            columns: new[] { "CompanyDb", "HashChaveGrupo" },
            unique: true);
    }
}
