using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialImport.Infrastructure.Migrations
{
/// <inheritdoc />
public partial class AddMessagingAndRichLogs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ===== ImportacaoArquivo — audit/correlation columns =====
        migrationBuilder.AddColumn<DateTime>(
            name: "AtualizadoEmUtc", table: "ImportacaoArquivo", type: "datetime(6)", nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ProcessamentoInicioUtc", table: "ImportacaoArquivo", type: "datetime(6)", nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ProcessamentoFimUtc", table: "ImportacaoArquivo", type: "datetime(6)", nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CorrelationId", table: "ImportacaoArquivo", type: "varchar(60)", maxLength: 60, nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Versao", table: "ImportacaoArquivo", type: "int", nullable: false, defaultValue: 0);

        migrationBuilder.CreateIndex(
            name: "IX_ImportacaoArquivo_Status", table: "ImportacaoArquivo", column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_ImportacaoArquivo_CorrelationId", table: "ImportacaoArquivo", column: "CorrelationId");

        // ===== ImportacaoLinha — group key + audit column =====
        migrationBuilder.AddColumn<string>(
            name: "HashChaveGrupo", table: "ImportacaoLinha", type: "varchar(64)", maxLength: 64, nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "AtualizadoEmUtc", table: "ImportacaoLinha", type: "datetime(6)", nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_ImportacaoLinha_Grupo",
            table: "ImportacaoLinha",
            columns: new[] { "ImportacaoArquivoId", "HashChaveGrupo" });

        migrationBuilder.CreateIndex(
            name: "IX_ImportacaoLinha_Status", table: "ImportacaoLinha", column: "Status");

        // ===== LogSistema — rich columns =====
        migrationBuilder.AddColumn<string>(
            name: "Categoria", table: "LogSistema", type: "varchar(30)", maxLength: 30, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Operacao", table: "LogSistema", type: "varchar(120)", maxLength: 120, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CausationId", table: "LogSistema", type: "varchar(60)", maxLength: 60, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MessageId", table: "LogSistema", type: "varchar(60)", maxLength: 60, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SapSessionId", table: "LogSistema", type: "varchar(120)", maxLength: 120, nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "ImportacaoArquivoId", table: "LogSistema", type: "bigint", nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "ImportacaoLinhaId", table: "LogSistema", type: "bigint", nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ChaveNegocio", table: "LogSistema", type: "varchar(200)", maxLength: 200, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StatusAntes", table: "LogSistema", type: "varchar(40)", maxLength: 40, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StatusDepois", table: "LogSistema", type: "varchar(40)", maxLength: 40, nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "DuracaoMs", table: "LogSistema", type: "bigint", nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "StackTrace", table: "LogSistema", type: "longtext", nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Hostname", table: "LogSistema", type: "varchar(120)", maxLength: 120, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Ambiente", table: "LogSistema", type: "varchar(40)", maxLength: 40, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Aplicacao", table: "LogSistema", type: "varchar(80)", maxLength: 80, nullable: true);

        // The Origem column needs to grow from 80 to 120 and Mensagem from 400 to 1024.
        migrationBuilder.AlterColumn<string>(
            name: "Origem", table: "LogSistema", type: "varchar(120)", maxLength: 120, nullable: false,
            oldClrType: typeof(string), oldType: "varchar(80)", oldMaxLength: 80);

        migrationBuilder.AlterColumn<string>(
            name: "Mensagem", table: "LogSistema", type: "varchar(1024)", maxLength: 1024, nullable: false,
            oldClrType: typeof(string), oldType: "varchar(400)", oldMaxLength: 400);

        migrationBuilder.CreateIndex(
            name: "IX_LogSistema_CorrelationId", table: "LogSistema", column: "CorrelationId");

        migrationBuilder.CreateIndex(
            name: "IX_LogSistema_Categoria_Nivel", table: "LogSistema", columns: new[] { "Categoria", "Nivel" });

        migrationBuilder.CreateIndex(
            name: "IX_LogSistema_ImportacaoArquivoId", table: "LogSistema", column: "ImportacaoArquivoId");

        // ===== MensagensOutbox =====
        migrationBuilder.CreateTable(
            name: "MensagensOutbox",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                Canal = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                TipoMensagem = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false),
                MessageId = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                CorrelationId = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                CausationId = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                Payload = table.Column<string>(type: "longtext", nullable: false),
                Broker = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                Status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                CriadoEmUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                EnviadoEmUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ProximaTentativaUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                ReservadoAteUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                QuantidadeTentativas = table.Column<int>(type: "int", nullable: false),
                UltimoErro = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                UsuarioId = table.Column<long>(type: "bigint", nullable: true),
                CompanyDb = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MensagensOutbox", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MensagensOutbox_MessageId", table: "MensagensOutbox", column: "MessageId", unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MensagensOutbox_Status_Proxima",
            table: "MensagensOutbox",
            columns: new[] { "Status", "ProximaTentativaUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_MensagensOutbox_CorrelationId", table: "MensagensOutbox", column: "CorrelationId");

        // ===== MensagensInbox =====
        migrationBuilder.CreateTable(
            name: "MensagensInbox",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                Consumidor = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                MessageId = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                ProcessadoEmUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                CorrelationId = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MensagensInbox", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MensagensInbox_Consumidor_MessageId",
            table: "MensagensInbox",
            columns: new[] { "Consumidor", "MessageId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_MensagensInbox_CorrelationId", table: "MensagensInbox", column: "CorrelationId");

        // ===== LancamentoSapDispatch =====
        migrationBuilder.CreateTable(
            name: "LancamentoSapDispatch",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                ImportacaoArquivoId = table.Column<long>(type: "bigint", nullable: false),
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
                table.PrimaryKey("PK_LancamentoSapDispatch", x => x.Id);
                table.ForeignKey(
                    name: "FK_LancamentoSapDispatch_ImportacaoArquivo_ImportacaoArquivoId",
                    column: x => x.ImportacaoArquivoId,
                    principalTable: "ImportacaoArquivo",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LancamentoSapDispatch_CompanyDb_HashChaveGrupo",
            table: "LancamentoSapDispatch",
            columns: new[] { "CompanyDb", "HashChaveGrupo" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_LancamentoSapDispatch_Status", table: "LancamentoSapDispatch", column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_LancamentoSapDispatch_Arquivo", table: "LancamentoSapDispatch", column: "ImportacaoArquivoId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LancamentoSapDispatch");
        migrationBuilder.DropTable(name: "MensagensInbox");
        migrationBuilder.DropTable(name: "MensagensOutbox");

        migrationBuilder.DropIndex(name: "IX_LogSistema_ImportacaoArquivoId", table: "LogSistema");
        migrationBuilder.DropIndex(name: "IX_LogSistema_Categoria_Nivel", table: "LogSistema");
        migrationBuilder.DropIndex(name: "IX_LogSistema_CorrelationId", table: "LogSistema");

        migrationBuilder.AlterColumn<string>(
            name: "Mensagem", table: "LogSistema", type: "varchar(400)", maxLength: 400, nullable: false,
            oldClrType: typeof(string), oldType: "varchar(1024)", oldMaxLength: 1024);

        migrationBuilder.AlterColumn<string>(
            name: "Origem", table: "LogSistema", type: "varchar(80)", maxLength: 80, nullable: false,
            oldClrType: typeof(string), oldType: "varchar(120)", oldMaxLength: 120);

        migrationBuilder.DropColumn(name: "Aplicacao", table: "LogSistema");
        migrationBuilder.DropColumn(name: "Ambiente", table: "LogSistema");
        migrationBuilder.DropColumn(name: "Hostname", table: "LogSistema");
        migrationBuilder.DropColumn(name: "StackTrace", table: "LogSistema");
        migrationBuilder.DropColumn(name: "DuracaoMs", table: "LogSistema");
        migrationBuilder.DropColumn(name: "StatusDepois", table: "LogSistema");
        migrationBuilder.DropColumn(name: "StatusAntes", table: "LogSistema");
        migrationBuilder.DropColumn(name: "ChaveNegocio", table: "LogSistema");
        migrationBuilder.DropColumn(name: "ImportacaoLinhaId", table: "LogSistema");
        migrationBuilder.DropColumn(name: "ImportacaoArquivoId", table: "LogSistema");
        migrationBuilder.DropColumn(name: "SapSessionId", table: "LogSistema");
        migrationBuilder.DropColumn(name: "MessageId", table: "LogSistema");
        migrationBuilder.DropColumn(name: "CausationId", table: "LogSistema");
        migrationBuilder.DropColumn(name: "Operacao", table: "LogSistema");
        migrationBuilder.DropColumn(name: "Categoria", table: "LogSistema");

        migrationBuilder.DropIndex(name: "IX_ImportacaoLinha_Status", table: "ImportacaoLinha");
        migrationBuilder.DropIndex(name: "IX_ImportacaoLinha_Grupo", table: "ImportacaoLinha");
        migrationBuilder.DropColumn(name: "AtualizadoEmUtc", table: "ImportacaoLinha");
        migrationBuilder.DropColumn(name: "HashChaveGrupo", table: "ImportacaoLinha");

        migrationBuilder.DropIndex(name: "IX_ImportacaoArquivo_CorrelationId", table: "ImportacaoArquivo");
        migrationBuilder.DropIndex(name: "IX_ImportacaoArquivo_Status", table: "ImportacaoArquivo");
        migrationBuilder.DropColumn(name: "Versao", table: "ImportacaoArquivo");
        migrationBuilder.DropColumn(name: "CorrelationId", table: "ImportacaoArquivo");
        migrationBuilder.DropColumn(name: "ProcessamentoFimUtc", table: "ImportacaoArquivo");
        migrationBuilder.DropColumn(name: "ProcessamentoInicioUtc", table: "ImportacaoArquivo");
        migrationBuilder.DropColumn(name: "AtualizadoEmUtc", table: "ImportacaoArquivo");
    }
}
}
