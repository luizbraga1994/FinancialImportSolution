using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialImport.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReferenciaIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ImportacaoLinha_Referencia",
                table: "ImportacaoLinha",
                column: "Referencia");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImportacaoLinha_Referencia",
                table: "ImportacaoLinha");
        }
    }
}
