using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace FinancialImport.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddMissingFkIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_PerfilPermissao_PermissaoId",
            table: "PerfilPermissao",
            column: "PermissaoId");

        migrationBuilder.CreateIndex(
            name: "IX_UsuarioPerfil_PerfilId",
            table: "UsuarioPerfil",
            column: "PerfilId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PerfilPermissao_PermissaoId",
            table: "PerfilPermissao");

        migrationBuilder.DropIndex(
            name: "IX_UsuarioPerfil_PerfilId",
            table: "UsuarioPerfil");
    }
}
