using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialImport.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddReceivableSettlement : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ===== BaixaArquivo (receivable settlement file) =====
        migrationBuilder.CreateTable(
            name: "BaixaArquivo",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                UsuarioId = table.Column<long>(type: "bigint", nullable: false),
                CompanyDb = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                NomeArquivoOriginal = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                HashArquivo = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                LayoutDetectado = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                QuantidadeLinhas = table.Column<int>(type: "int", nullable: false),
                QuantidadeValidas = table.Column<int>(type: "int", nullable: false),
                QuantidadeInvalidas = table.Column<int>(type: "int", nullable: false),
                QuantidadeBaixadas = table.Column<int>(type: "int", nullable: false),
                QuantidadeDuplicadas = table.Column<int>(type: "int", nullable: false),
                QuantidadeComErro = table.Column<int>(type: "int", nullable: false),
                DataImportacao = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                AtualizadoEmUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ProcessamentoInicioUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ProcessamentoFimUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                CorrelationId = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                Versao = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BaixaArquivo", x => x.Id);
                table.ForeignKey(
                    name: "FK_BaixaArquivo_Usuarios_UsuarioId",
                    column: x => x.UsuarioId,
                    principalTable: "Usuarios",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        // ===== BaixaLinha (settlement line) =====
        migrationBuilder.CreateTable(
            name: "BaixaLinha",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                BaixaArquivoId = table.Column<long>(type: "bigint", nullable: false),
                HashLinha = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                HashChaveNegocio = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                DocPN = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                NumeroNota = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                Serie = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true),
                CnpjFilial = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                Modelo = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                FormaPagamento = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                ContaContabil = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                Valor = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Desconto = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Juros = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                DataPagamento = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                QtdParcelas = table.Column<int>(type: "int", nullable: false),
                Bandeira = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true),
                Ultimo4Cartao = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                Referencia = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                CardCode = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                DocEntryFatura = table.Column<int>(type: "int", nullable: true),
                VoucherNum = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                BPLId = table.Column<int>(type: "int", nullable: true),
                CompanyDb = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                MensagemValidacao = table.Column<string>(type: "varchar(400)", maxLength: 400, nullable: true),
                MensagemRetornoSap = table.Column<string>(type: "varchar(400)", maxLength: 400, nullable: true),
                DocEntrySap = table.Column<int>(type: "int", nullable: true),
                JsonOrigem = table.Column<string>(type: "json", nullable: true),
                HashChaveGrupo = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                AtualizadoEmUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BaixaLinha", x => x.Id);
                table.ForeignKey(
                    name: "FK_BaixaLinha_BaixaArquivo_BaixaArquivoId",
                    column: x => x.BaixaArquivoId,
                    principalTable: "BaixaArquivo",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        // ===== BaixaSapDispatch (incoming payment dispatch tracking) =====
        migrationBuilder.CreateTable(
            name: "BaixaSapDispatch",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                BaixaArquivoId = table.Column<long>(type: "bigint", nullable: false),
                CompanyDb = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                HashChaveGrupo = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                ChaveGrupo = table.Column<string>(type: "varchar(400)", maxLength: 400, nullable: false),
                Status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                QuantidadeTentativas = table.Column<int>(type: "int", nullable: false),
                CriadoEmUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                EnviadoEmUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                UltimaTentativaUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                DocEntrySap = table.Column<int>(type: "int", nullable: true),
                RespostaSap = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                UltimoErro = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                CorrelationId = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BaixaSapDispatch", x => x.Id);
                table.ForeignKey(
                    name: "FK_BaixaSapDispatch_BaixaArquivo_BaixaArquivoId",
                    column: x => x.BaixaArquivoId,
                    principalTable: "BaixaArquivo",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        // ===== MapeamentoBandeiraCartao (card brand -> SAP credit card master) =====
        migrationBuilder.CreateTable(
            name: "MapeamentoBandeiraCartao",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
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

        // ===== Indexes =====
        migrationBuilder.CreateIndex(
            name: "IX_BaixaArquivo_CompanyDb_HashArquivo",
            table: "BaixaArquivo",
            columns: new[] { "CompanyDb", "HashArquivo" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BaixaArquivo_UsuarioId", table: "BaixaArquivo", column: "UsuarioId");

        migrationBuilder.CreateIndex(
            name: "IX_BaixaArquivo_CompanyDb", table: "BaixaArquivo", column: "CompanyDb");

        migrationBuilder.CreateIndex(
            name: "IX_BaixaArquivo_Status", table: "BaixaArquivo", column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_BaixaArquivo_CorrelationId", table: "BaixaArquivo", column: "CorrelationId");

        migrationBuilder.CreateIndex(
            name: "IX_BaixaLinha_FileId_HashChaveNegocio",
            table: "BaixaLinha",
            columns: new[] { "BaixaArquivoId", "HashChaveNegocio" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BaixaLinha_BaixaArquivoId", table: "BaixaLinha", column: "BaixaArquivoId");

        migrationBuilder.CreateIndex(
            name: "IX_BaixaLinha_Grupo",
            table: "BaixaLinha",
            columns: new[] { "BaixaArquivoId", "HashChaveGrupo" });

        migrationBuilder.CreateIndex(
            name: "IX_BaixaLinha_Status", table: "BaixaLinha", column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_BaixaLinha_FileIdGroupStatus",
            table: "BaixaLinha",
            columns: new[] { "BaixaArquivoId", "HashChaveGrupo", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_BaixaSapDispatch_FileId_GroupKeyHash",
            table: "BaixaSapDispatch",
            columns: new[] { "BaixaArquivoId", "HashChaveGrupo" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BaixaSapDispatch_Status", table: "BaixaSapDispatch", column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_BaixaSapDispatch_Arquivo", table: "BaixaSapDispatch", column: "BaixaArquivoId");

        migrationBuilder.CreateIndex(
            name: "IX_MapeamentoBandeiraCartao_CompanyDb_Bandeira",
            table: "MapeamentoBandeiraCartao",
            columns: new[] { "CompanyDb", "Bandeira" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "BaixaLinha");
        migrationBuilder.DropTable(name: "BaixaSapDispatch");
        migrationBuilder.DropTable(name: "MapeamentoBandeiraCartao");
        migrationBuilder.DropTable(name: "BaixaArquivo");
    }
}
