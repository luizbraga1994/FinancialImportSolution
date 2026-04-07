using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace FinancialImport.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ===== Usuarios =====
        migrationBuilder.CreateTable(
            name: "Usuarios",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                Login = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                Nome = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                Email = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                SenhaHash = table.Column<byte[]>(type: "varbinary(256)", nullable: false),
                SenhaSalt = table.Column<byte[]>(type: "varbinary(128)", nullable: true),
                Ativo = table.Column<bool>(type: "tinyint(1)", nullable: false),
                Bloqueado = table.Column<bool>(type: "tinyint(1)", nullable: false),
                AdminGlobal = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                DataCriacao = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                DataUltimoLogin = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                UsuarioCriacao = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Usuarios", x => x.Id);
            });

        migrationBuilder.CreateIndex(name: "IX_Usuarios_Login", table: "Usuarios", column: "Login", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Usuarios_Email", table: "Usuarios", column: "Email", unique: true);

        // ===== Perfis =====
        migrationBuilder.CreateTable(
            name: "Perfis",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                Nome = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                Descricao = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                Ativo = table.Column<bool>(type: "tinyint(1)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Perfis", x => x.Id);
            });

        migrationBuilder.CreateIndex(name: "IX_Perfis_Nome", table: "Perfis", column: "Nome", unique: true);

        // ===== Permissoes =====
        migrationBuilder.CreateTable(
            name: "Permissoes",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                Codigo = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                Nome = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                Descricao = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                Grupo = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                Ativo = table.Column<bool>(type: "tinyint(1)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Permissoes", x => x.Id);
            });

        migrationBuilder.CreateIndex(name: "IX_Permissoes_Codigo", table: "Permissoes", column: "Codigo", unique: true);

        // ===== UsuarioPerfil =====
        migrationBuilder.CreateTable(
            name: "UsuarioPerfil",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                UsuarioId = table.Column<long>(type: "bigint", nullable: false),
                PerfilId = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UsuarioPerfil", x => x.Id);
                table.ForeignKey(name: "FK_UsuarioPerfil_Usuarios_UsuarioId", column: x => x.UsuarioId, principalTable: "Usuarios", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey(name: "FK_UsuarioPerfil_Perfis_PerfilId", column: x => x.PerfilId, principalTable: "Perfis", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_UsuarioPerfil_UsuarioId_PerfilId", table: "UsuarioPerfil", columns: new[] { "UsuarioId", "PerfilId" }, unique: true);

        // ===== PerfilPermissao =====
        migrationBuilder.CreateTable(
            name: "PerfilPermissao",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                PerfilId = table.Column<long>(type: "bigint", nullable: false),
                PermissaoId = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PerfilPermissao", x => x.Id);
                table.ForeignKey(name: "FK_PerfilPermissao_Perfis_PerfilId", column: x => x.PerfilId, principalTable: "Perfis", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey(name: "FK_PerfilPermissao_Permissoes_PermissaoId", column: x => x.PermissaoId, principalTable: "Permissoes", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_PerfilPermissao_PerfilId_PermissaoId", table: "PerfilPermissao", columns: new[] { "PerfilId", "PermissaoId" }, unique: true);

        // ===== UsuarioEmpresaPermitida =====
        migrationBuilder.CreateTable(
            name: "UsuarioEmpresaPermitida",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                UsuarioId = table.Column<long>(type: "bigint", nullable: false),
                CompanyDb = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                Ativo = table.Column<bool>(type: "tinyint(1)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UsuarioEmpresaPermitida", x => x.Id);
                table.ForeignKey(name: "FK_UsuarioEmpresaPermitida_Usuarios_UsuarioId", column: x => x.UsuarioId, principalTable: "Usuarios", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_UsuarioEmpresaPermitida_UsuarioId_CompanyDb", table: "UsuarioEmpresaPermitida", columns: new[] { "UsuarioId", "CompanyDb" }, unique: true);

        // ===== AuditoriaLogin =====
        migrationBuilder.CreateTable(
            name: "AuditoriaLogin",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                UsuarioId = table.Column<long>(type: "bigint", nullable: true),
                LoginInformado = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                Sucesso = table.Column<bool>(type: "tinyint(1)", nullable: false),
                EnderecoIp = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                UserAgent = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                DataHora = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                MotivoFalha = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditoriaLogin", x => x.Id);
                table.ForeignKey(name: "FK_AuditoriaLogin_Usuarios_UsuarioId", column: x => x.UsuarioId, principalTable: "Usuarios", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(name: "IX_AuditoriaLogin_DataHora", table: "AuditoriaLogin", column: "DataHora");

        // ===== SessaoEmpresaUsuario =====
        migrationBuilder.CreateTable(
            name: "SessaoEmpresaUsuario",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                UsuarioId = table.Column<long>(type: "bigint", nullable: false),
                CompanyDb = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                CompanyName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                SapUserName = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                SessionId = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                RouteId = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true),
                ExpiraEm = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                Ativa = table.Column<bool>(type: "tinyint(1)", nullable: false),
                DataLogin = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SessaoEmpresaUsuario", x => x.Id);
                table.ForeignKey(name: "FK_SessaoEmpresaUsuario_Usuarios_UsuarioId", column: x => x.UsuarioId, principalTable: "Usuarios", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_SessaoEmpresaUsuario_UsuarioId", table: "SessaoEmpresaUsuario", column: "UsuarioId");
        migrationBuilder.CreateIndex(name: "IX_SessaoEmpresaUsuario_CompanyDb", table: "SessaoEmpresaUsuario", column: "CompanyDb");

        // ===== ImportacaoArquivo =====
        migrationBuilder.CreateTable(
            name: "ImportacaoArquivo",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                UsuarioId = table.Column<long>(type: "bigint", nullable: false),
                CompanyDb = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                NomeArquivoOriginal = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                HashArquivo = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                LayoutDetectado = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                FilialPadrao = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                UsarFilialArquivo = table.Column<bool>(type: "tinyint(1)", nullable: false),
                Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                QuantidadeLinhas = table.Column<int>(type: "int", nullable: false),
                QuantidadeValidas = table.Column<int>(type: "int", nullable: false),
                QuantidadeInvalidas = table.Column<int>(type: "int", nullable: false),
                QuantidadeImportadas = table.Column<int>(type: "int", nullable: false),
                QuantidadeDuplicadas = table.Column<int>(type: "int", nullable: false),
                QuantidadeComErro = table.Column<int>(type: "int", nullable: false),
                DataImportacao = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ImportacaoArquivo", x => x.Id);
                table.ForeignKey(name: "FK_ImportacaoArquivo_Usuarios_UsuarioId", column: x => x.UsuarioId, principalTable: "Usuarios", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_ImportacaoArquivo_CompanyDb_HashArquivo", table: "ImportacaoArquivo", columns: new[] { "CompanyDb", "HashArquivo" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_ImportacaoArquivo_UsuarioId", table: "ImportacaoArquivo", column: "UsuarioId");
        migrationBuilder.CreateIndex(name: "IX_ImportacaoArquivo_CompanyDb", table: "ImportacaoArquivo", column: "CompanyDb");

        // ===== ImportacaoLinha =====
        migrationBuilder.CreateTable(
            name: "ImportacaoLinha",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                ImportacaoArquivoId = table.Column<long>(type: "bigint", nullable: false),
                HashLinha = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                HashChaveNegocio = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                SeqLancamento = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                Referencia = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                ContaContabil = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                ContaContrapartida = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                DataLancamento = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                DataVencimento = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                DataDocumento = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                Valor = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                ValorCredito = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                ValorDebito = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                HistoricoLinha = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                Filial = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                CompanyDb = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                MensagemValidacao = table.Column<string>(type: "varchar(400)", maxLength: 400, nullable: true),
                MensagemRetornoSap = table.Column<string>(type: "varchar(400)", maxLength: 400, nullable: true),
                DocEntrySap = table.Column<int>(type: "int", nullable: true),
                JsonOrigem = table.Column<string>(type: "json", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ImportacaoLinha", x => x.Id);
                table.ForeignKey(name: "FK_ImportacaoLinha_ImportacaoArquivo_ImportacaoArquivoId", column: x => x.ImportacaoArquivoId, principalTable: "ImportacaoArquivo", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_ImportacaoLinha_CompanyDb_HashChaveNegocio", table: "ImportacaoLinha", columns: new[] { "CompanyDb", "HashChaveNegocio" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_ImportacaoLinha_ImportacaoArquivoId", table: "ImportacaoLinha", column: "ImportacaoArquivoId");

        // ===== LogSistema =====
        migrationBuilder.CreateTable(
            name: "LogSistema",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                DataHora = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                Nivel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                Origem = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                UsuarioId = table.Column<long>(type: "bigint", nullable: true),
                CompanyDb = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                CorrelationId = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                Mensagem = table.Column<string>(type: "varchar(400)", maxLength: 400, nullable: false),
                Detalhes = table.Column<string>(type: "longtext", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LogSistema", x => x.Id);
            });

        migrationBuilder.CreateIndex(name: "IX_LogSistema_DataHora", table: "LogSistema", column: "DataHora");
        migrationBuilder.CreateIndex(name: "IX_LogSistema_UsuarioId", table: "LogSistema", column: "UsuarioId");

        // ===== MapeamentoFilialSap =====
        migrationBuilder.CreateTable(
            name: "MapeamentoFilialSap",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                CompanyDb = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                CodigoFilialArquivo = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                BPLId = table.Column<int>(type: "int", nullable: false),
                NomeFilial = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                Ativo = table.Column<bool>(type: "tinyint(1)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MapeamentoFilialSap", x => x.Id);
            });

        migrationBuilder.CreateIndex(name: "IX_MapeamentoFilialSap_CompanyDb_CodigoFilialArquivo", table: "MapeamentoFilialSap", columns: new[] { "CompanyDb", "CodigoFilialArquivo" }, unique: true);

        // ===== ConfiguracaoLayout =====
        migrationBuilder.CreateTable(
            name: "ConfiguracaoLayout",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                NomeLayout = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                Ativo = table.Column<bool>(type: "tinyint(1)", nullable: false),
                Descricao = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConfiguracaoLayout", x => x.Id);
            });

        migrationBuilder.CreateIndex(name: "IX_ConfiguracaoLayout_NomeLayout", table: "ConfiguracaoLayout", column: "NomeLayout", unique: true);

        // ===== ConfiguracaoLayoutCampo =====
        migrationBuilder.CreateTable(
            name: "ConfiguracaoLayoutCampo",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                LayoutId = table.Column<long>(type: "bigint", nullable: false),
                NomeColunaOrigem = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                NomeCampoInterno = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                Obrigatorio = table.Column<bool>(type: "tinyint(1)", nullable: false),
                TipoDado = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                Ordem = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConfiguracaoLayoutCampo", x => x.Id);
                table.ForeignKey(name: "FK_ConfiguracaoLayoutCampo_ConfiguracaoLayout_LayoutId", column: x => x.LayoutId, principalTable: "ConfiguracaoLayout", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });

        // ===== Regras =====
        migrationBuilder.CreateTable(
            name: "Regras",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                Chave = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                Valor = table.Column<string>(type: "varchar(400)", maxLength: 400, nullable: false),
                EscopoCompanyDb = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                Ativo = table.Column<bool>(type: "tinyint(1)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Regras", x => x.Id);
            });

        migrationBuilder.CreateIndex(name: "IX_Regras_Chave", table: "Regras", column: "Chave", unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ConfiguracaoLayoutCampo");
        migrationBuilder.DropTable(name: "ConfiguracaoLayout");
        migrationBuilder.DropTable(name: "ImportacaoLinha");
        migrationBuilder.DropTable(name: "ImportacaoArquivo");
        migrationBuilder.DropTable(name: "PerfilPermissao");
        migrationBuilder.DropTable(name: "UsuarioPerfil");
        migrationBuilder.DropTable(name: "UsuarioEmpresaPermitida");
        migrationBuilder.DropTable(name: "AuditoriaLogin");
        migrationBuilder.DropTable(name: "SessaoEmpresaUsuario");
        migrationBuilder.DropTable(name: "LogSistema");
        migrationBuilder.DropTable(name: "MapeamentoFilialSap");
        migrationBuilder.DropTable(name: "Regras");
        migrationBuilder.DropTable(name: "Permissoes");
        migrationBuilder.DropTable(name: "Perfis");
        migrationBuilder.DropTable(name: "Usuarios");
    }
}
