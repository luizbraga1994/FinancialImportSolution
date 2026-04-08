using FinancialImport.Domain.Entities;
using FinancialImport.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FinancialImport.Infrastructure.Data;

public sealed class AppDbContext : DbContext
{
    private readonly ILogger<AppDbContext>? _logger;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<ProfilePermission> ProfilePermissions => Set<ProfilePermission>();
    public DbSet<UserCompanyPermission> UserCompanyPermissions => Set<UserCompanyPermission>();
    public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();
    public DbSet<CompanyUserSession> CompanyUserSessions => Set<CompanyUserSession>();
    public DbSet<ImportFile> ImportFiles => Set<ImportFile>();
    public DbSet<ImportLine> ImportLines => Set<ImportLine>();
    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();
    public DbSet<BranchMapping> BranchMappings => Set<BranchMapping>();
    public DbSet<LayoutConfig> LayoutConfigs => Set<LayoutConfig>();
    public DbSet<LayoutFieldConfig> LayoutFieldConfigs => Set<LayoutFieldConfig>();
    public DbSet<Rule> Rules => Set<Rule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Usuarios");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.Login).HasColumnName("Login").HasMaxLength(80).IsRequired();
            entity.Property(e => e.Name).HasColumnName("Nome").HasMaxLength(120).IsRequired();
            entity.Property(e => e.Email).HasColumnName("Email").HasMaxLength(120).IsRequired();
            entity.Property(e => e.PasswordHash).HasColumnName("SenhaHash").HasColumnType("varbinary(256)").IsRequired();
            entity.Property(e => e.PasswordSalt).HasColumnName("SenhaSalt").HasColumnType("varbinary(128)");
            entity.Property(e => e.IsActive).HasColumnName("Ativo").IsRequired();
            entity.Property(e => e.IsBlocked).HasColumnName("Bloqueado").IsRequired();
            entity.Property(e => e.IsGlobalAdmin).HasColumnName("AdminGlobal").IsRequired().HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasColumnName("DataCriacao").IsRequired();
            entity.Property(e => e.LastLoginAt).HasColumnName("DataUltimoLogin");
            entity.Property(e => e.CreatedBy).HasColumnName("UsuarioCriacao").HasMaxLength(80).IsRequired();
            entity.HasIndex(e => e.Login).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        modelBuilder.Entity<Profile>(entity =>
        {
            entity.ToTable("Perfis");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.Name).HasColumnName("Nome").HasMaxLength(80).IsRequired();
            entity.Property(e => e.Description).HasColumnName("Descricao").HasMaxLength(200);
            entity.Property(e => e.IsActive).HasColumnName("Ativo").IsRequired();
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.ToTable("Permissoes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.Code).HasColumnName("Codigo").HasMaxLength(80).IsRequired();
            entity.Property(e => e.Name).HasColumnName("Nome").HasMaxLength(120).IsRequired();
            entity.Property(e => e.Description).HasColumnName("Descricao").HasMaxLength(200);
            entity.Property(e => e.Group).HasColumnName("Grupo").HasMaxLength(80);
            entity.Property(e => e.IsActive).HasColumnName("Ativo").IsRequired();
            entity.HasIndex(e => e.Code).IsUnique();
        });

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("UsuarioPerfil");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.UserId).HasColumnName("UsuarioId").IsRequired();
            entity.Property(e => e.ProfileId).HasColumnName("PerfilId").IsRequired();
            entity.HasOne(e => e.User).WithMany(u => u.Profiles).HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Profile).WithMany(p => p.Users).HasForeignKey(e => e.ProfileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.ProfileId }).IsUnique();
        });

        modelBuilder.Entity<ProfilePermission>(entity =>
        {
            entity.ToTable("PerfilPermissao");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.ProfileId).HasColumnName("PerfilId").IsRequired();
            entity.Property(e => e.PermissionId).HasColumnName("PermissaoId").IsRequired();
            entity.HasOne(e => e.Profile).WithMany(p => p.Permissions).HasForeignKey(e => e.ProfileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Permission).WithMany(p => p.Profiles).HasForeignKey(e => e.PermissionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ProfileId, e.PermissionId }).IsUnique();
        });

        modelBuilder.Entity<UserCompanyPermission>(entity =>
        {
            entity.ToTable("UsuarioEmpresaPermitida");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.UserId).HasColumnName("UsuarioId").IsRequired();
            entity.Property(e => e.CompanyDb).HasColumnName("CompanyDb").HasMaxLength(50).IsRequired();
            entity.Property(e => e.IsActive).HasColumnName("Ativo").IsRequired();
            entity.HasOne(e => e.User).WithMany(u => u.AllowedCompanies).HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.UserId, e.CompanyDb }).IsUnique();
        });

        modelBuilder.Entity<LoginAudit>(entity =>
        {
            entity.ToTable("AuditoriaLogin");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.UserId).HasColumnName("UsuarioId");
            entity.Property(e => e.LoginProvided).HasColumnName("LoginInformado").HasMaxLength(80).IsRequired();
            entity.Property(e => e.Success).HasColumnName("Sucesso").IsRequired();
            entity.Property(e => e.IpAddress).HasColumnName("EnderecoIp").HasMaxLength(64);
            entity.Property(e => e.UserAgent).HasColumnName("UserAgent").HasMaxLength(200);
            entity.Property(e => e.OccurredAt).HasColumnName("DataHora").IsRequired();
            entity.Property(e => e.FailureReason).HasColumnName("MotivoFalha").HasMaxLength(200);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.OccurredAt);
        });

        modelBuilder.Entity<CompanyUserSession>(entity =>
        {
            entity.ToTable("SessaoEmpresaUsuario");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.UserId).HasColumnName("UsuarioId").IsRequired();
            entity.Property(e => e.CompanyDb).HasColumnName("CompanyDb").HasMaxLength(50).IsRequired();
            entity.Property(e => e.CompanyName).HasColumnName("CompanyName").HasMaxLength(120).IsRequired();
            entity.Property(e => e.SapUserName).HasColumnName("SapUserName").HasMaxLength(80).IsRequired();
            entity.Property(e => e.SessionId).HasColumnName("SessionId").HasMaxLength(120).IsRequired();
            entity.Property(e => e.RouteId).HasColumnName("RouteId").HasMaxLength(120);
            entity.Property(e => e.ExpiresAt).HasColumnName("ExpiraEm").IsRequired();
            entity.Property(e => e.IsActive).HasColumnName("Ativa").IsRequired();
            entity.Property(e => e.LoginAt).HasColumnName("DataLogin").IsRequired();
            entity.HasOne(e => e.User).WithMany(u => u.CompanySessions).HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CompanyDb);
        });

        modelBuilder.Entity<ImportFile>(entity =>
        {
            entity.ToTable("ImportacaoArquivo");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.UserId).HasColumnName("UsuarioId").IsRequired();
            entity.Property(e => e.CompanyDb).HasColumnName("CompanyDb").HasMaxLength(50).IsRequired();
            entity.Property(e => e.OriginalFileName).HasColumnName("NomeArquivoOriginal").HasMaxLength(200).IsRequired();
            entity.Property(e => e.FileHash).HasColumnName("HashArquivo").HasMaxLength(64).IsRequired();
            entity.Property(e => e.LayoutDetected).HasColumnName("LayoutDetectado").HasMaxLength(80).IsRequired();
            entity.Property(e => e.BranchDefault).HasColumnName("FilialPadrao").HasMaxLength(20);
            entity.Property(e => e.UseBranchFromFile).HasColumnName("UsarFilialArquivo").IsRequired();
            entity.Property(e => e.Status).HasColumnName("Status").HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(e => e.TotalLines).HasColumnName("QuantidadeLinhas").IsRequired();
            entity.Property(e => e.ValidLines).HasColumnName("QuantidadeValidas").IsRequired();
            entity.Property(e => e.InvalidLines).HasColumnName("QuantidadeInvalidas").IsRequired();
            entity.Property(e => e.ImportedLines).HasColumnName("QuantidadeImportadas").IsRequired();
            entity.Property(e => e.DuplicatedLines).HasColumnName("QuantidadeDuplicadas").IsRequired();
            entity.Property(e => e.LinesWithError).HasColumnName("QuantidadeComErro").IsRequired();
            entity.Property(e => e.ImportedAt).HasColumnName("DataImportacao").IsRequired();
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.CompanyDb, e.FileHash }).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CompanyDb);
        });

        modelBuilder.Entity<ImportLine>(entity =>
        {
            entity.ToTable("ImportacaoLinha");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.ImportFileId).HasColumnName("ImportacaoArquivoId").IsRequired();
            entity.Property(e => e.LineHash).HasColumnName("HashLinha").HasMaxLength(64).IsRequired();
            entity.Property(e => e.BusinessKeyHash).HasColumnName("HashChaveNegocio").HasMaxLength(64).IsRequired();
            entity.Property(e => e.SeqLancamento).HasColumnName("SeqLancamento").HasMaxLength(60);
            entity.Property(e => e.Reference).HasColumnName("Referencia").HasMaxLength(120).IsRequired();
            entity.Property(e => e.AccountCode).HasColumnName("ContaContabil").HasMaxLength(30).IsRequired();
            entity.Property(e => e.ContraAccountCode).HasColumnName("ContaContrapartida").HasMaxLength(30).IsRequired();
            entity.Property(e => e.PostingDate).HasColumnName("DataLancamento").IsRequired();
            entity.Property(e => e.DueDate).HasColumnName("DataVencimento").IsRequired();
            entity.Property(e => e.DocumentDate).HasColumnName("DataDocumento").IsRequired();
            entity.Property(e => e.Amount).HasColumnName("Valor").HasColumnType("decimal(18,2)").IsRequired();
            entity.Property(e => e.CreditAmount).HasColumnName("ValorCredito").HasColumnType("decimal(18,2)");
            entity.Property(e => e.DebitAmount).HasColumnName("ValorDebito").HasColumnType("decimal(18,2)");
            entity.Property(e => e.LineMemo).HasColumnName("HistoricoLinha").HasMaxLength(200).IsRequired();
            entity.Property(e => e.BranchCode).HasColumnName("Filial").HasMaxLength(20);
            entity.Property(e => e.CompanyDb).HasColumnName("CompanyDb").HasMaxLength(50).IsRequired();
            entity.Property(e => e.Status).HasColumnName("Status").HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(e => e.ValidationMessage).HasColumnName("MensagemValidacao").HasMaxLength(400);
            entity.Property(e => e.SapReturnMessage).HasColumnName("MensagemRetornoSap").HasMaxLength(400);
            entity.Property(e => e.SapDocEntry).HasColumnName("DocEntrySap");
            entity.Property(e => e.SourceJson).HasColumnName("JsonOrigem").HasColumnType("json");
            entity.HasOne(e => e.ImportFile).WithMany(f => f.Lines).HasForeignKey(e => e.ImportFileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.CompanyDb, e.BusinessKeyHash }).IsUnique();
            entity.HasIndex(e => e.ImportFileId);
            entity.HasIndex(e => e.Reference).HasDatabaseName("IX_ImportacaoLinha_Referencia");
        });

        modelBuilder.Entity<SystemLog>(entity =>
        {
            entity.ToTable("LogSistema");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.OccurredAt).HasColumnName("DataHora").IsRequired();
            entity.Property(e => e.Level).HasColumnName("Nivel").HasMaxLength(20).IsRequired();
            entity.Property(e => e.Source).HasColumnName("Origem").HasMaxLength(80).IsRequired();
            entity.Property(e => e.UserId).HasColumnName("UsuarioId");
            entity.Property(e => e.CompanyDb).HasColumnName("CompanyDb").HasMaxLength(50);
            entity.Property(e => e.CorrelationId).HasColumnName("CorrelationId").HasMaxLength(60);
            entity.Property(e => e.Message).HasColumnName("Mensagem").HasMaxLength(400).IsRequired();
            entity.Property(e => e.Details).HasColumnName("Detalhes");
            entity.HasIndex(e => e.OccurredAt);
            entity.HasIndex(e => e.UserId);
        });

        modelBuilder.Entity<BranchMapping>(entity =>
        {
            entity.ToTable("MapeamentoFilialSap");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.CompanyDb).HasColumnName("CompanyDb").HasMaxLength(50).IsRequired();
            entity.Property(e => e.FileBranchCode).HasColumnName("CodigoFilialArquivo").HasMaxLength(20).IsRequired();
            entity.Property(e => e.BplId).HasColumnName("BPLId").IsRequired();
            entity.Property(e => e.BranchName).HasColumnName("NomeFilial").HasMaxLength(120).IsRequired();
            entity.Property(e => e.IsActive).HasColumnName("Ativo").IsRequired();
            entity.HasIndex(e => new { e.CompanyDb, e.FileBranchCode }).IsUnique();
        });

        modelBuilder.Entity<LayoutConfig>(entity =>
        {
            entity.ToTable("ConfiguracaoLayout");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.LayoutName).HasColumnName("NomeLayout").HasMaxLength(80).IsRequired();
            entity.Property(e => e.IsActive).HasColumnName("Ativo").IsRequired();
            entity.Property(e => e.Description).HasColumnName("Descricao").HasMaxLength(200);
            entity.HasIndex(e => e.LayoutName).IsUnique();
        });

        modelBuilder.Entity<LayoutFieldConfig>(entity =>
        {
            entity.ToTable("ConfiguracaoLayoutCampo");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.LayoutId).HasColumnName("LayoutId").IsRequired();
            entity.Property(e => e.SourceColumnName).HasColumnName("NomeColunaOrigem").HasMaxLength(120).IsRequired();
            entity.Property(e => e.InternalFieldName).HasColumnName("NomeCampoInterno").HasMaxLength(120).IsRequired();
            entity.Property(e => e.IsRequired).HasColumnName("Obrigatorio").IsRequired();
            entity.Property(e => e.DataType).HasColumnName("TipoDado").HasMaxLength(40).IsRequired();
            entity.Property(e => e.Order).HasColumnName("Ordem").IsRequired();
            entity.HasOne(e => e.Layout).WithMany(l => l.Fields).HasForeignKey(e => e.LayoutId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Rule>(entity =>
        {
            entity.ToTable("Regras");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.Key).HasColumnName("Chave").HasMaxLength(120).IsRequired();
            entity.Property(e => e.Value).HasColumnName("Valor").HasMaxLength(400).IsRequired();
            entity.Property(e => e.ScopeCompanyDb).HasColumnName("EscopoCompanyDb").HasMaxLength(50);
            entity.Property(e => e.IsActive).HasColumnName("Ativo").IsRequired();
            entity.HasIndex(e => e.Key).IsUnique();
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Validações antes de salvar
            ValidateEntities();

            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            var innerMessage = ex.InnerException?.Message ?? ex.Message;

            // Log do erro detalhado
            var logger = this.GetService<ILogger<AppDbContext>>();
            logger?.LogError(ex, "Erro ao salvar alterações no banco. Detalhe: {InnerMessage}", innerMessage);

            // Identifica o tipo de erro para mensagem mais amigável
            if (innerMessage.Contains("Duplicate entry") || innerMessage.Contains("UNIQUE constraint failed"))
            {
                if (innerMessage.Contains("HashChaveNegocio"))
                {
                    throw new DbUpdateException("Linha duplicada: esta combinação de dados já foi importada anteriormente.", ex);
                }
                if (innerMessage.Contains("HashArquivo"))
                {
                    throw new DbUpdateException("Arquivo duplicado: este arquivo já foi importado para esta empresa.", ex);
                }
                throw new DbUpdateException("Registro duplicado. Verifique se os dados já não foram importados.", ex);
            }

            if (innerMessage.Contains("Data too long") || innerMessage.Contains("String or binary data would be truncated"))
            {
                throw new DbUpdateException("Um ou mais campos excederam o tamanho máximo permitido. Verifique os dados da planilha.", ex);
            }

            if (innerMessage.Contains("JSON") || innerMessage.Contains("json"))
            {
                throw new DbUpdateException("Erro no formato JSON dos dados. Entre em contato com o suporte.", ex);
            }

            throw;
        }
    }

    private void ValidateEntities()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                // Validação para ImportLine
                if (entry.Entity is ImportLine line)
                {
                    if (!string.IsNullOrEmpty(line.SourceJson) && line.SourceJson.Length > 65535)
                    {
                        throw new InvalidOperationException($"JSON da linha {line.Id} excede o tamanho máximo de 65535 caracteres.");
                    }

                    if (!string.IsNullOrEmpty(line.ValidationMessage) && line.ValidationMessage.Length > 400)
                    {
                        line.ValidationMessage = line.ValidationMessage.Substring(0, 397) + "...";
                    }

                    if (!string.IsNullOrEmpty(line.SapReturnMessage) && line.SapReturnMessage.Length > 400)
                    {
                        line.SapReturnMessage = line.SapReturnMessage.Substring(0, 397) + "...";
                    }

                    if (!string.IsNullOrEmpty(line.LineMemo) && line.LineMemo.Length > 200)
                    {
                        line.LineMemo = line.LineMemo.Substring(0, 197) + "...";
                    }

                    if (!string.IsNullOrEmpty(line.Reference) && line.Reference.Length > 120)
                    {
                        line.Reference = line.Reference.Substring(0, 117) + "...";
                    }
                }

                // Validação para ImportFile
                if (entry.Entity is ImportFile file)
                {
                    if (!string.IsNullOrEmpty(file.OriginalFileName) && file.OriginalFileName.Length > 200)
                    {
                        file.OriginalFileName = file.OriginalFileName.Substring(0, 197) + "...";
                    }
                }
            }
        }
    }
}