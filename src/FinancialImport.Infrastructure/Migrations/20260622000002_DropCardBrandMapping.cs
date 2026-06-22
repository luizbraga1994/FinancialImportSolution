using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialImport.Infrastructure.Migrations;

/// <summary>
/// The card brand de/para is now resolved live from SAP HANA (OCRC for the
/// CreditCard/AcctCode and OCRP for the payment-method code), so the manually
/// maintained MapeamentoBandeiraCartao table is no longer needed.
/// </summary>
public partial class DropCardBrandMapping : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MapeamentoBandeiraCartao");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MapeamentoBandeiraCartao",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", Microsoft.EntityFrameworkCore.Metadata.MySqlValueGenerationStrategy.IdentityColumn),
                CompanyDb = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                Bandeira = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                CodigoCartaoSap = table.Column<int>(type: "int", nullable: false),
                CodigoMeioPagamento = table.Column<int>(type: "int", nullable: false),
                ContaCartao = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                Ativo = table.Column<bool>(type: "tinyint(1)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MapeamentoBandeiraCartao", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MapeamentoBandeiraCartao_CompanyDb_Bandeira",
            table: "MapeamentoBandeiraCartao",
            columns: new[] { "CompanyDb", "Bandeira" },
            unique: true);
    }
}
