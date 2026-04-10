using FinancialImport.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinancialImport.Infrastructure.Data;

public sealed class AppDbContext : DbContext
{
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
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<JournalEntryDispatch> JournalEntryDispatches => Set<JournalEntryDispatch>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

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
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("AtualizadoEmUtc");
            entity.Property(e => e.ProcessingStartedAtUtc).HasColumnName("ProcessamentoInicioUtc");
            entity.Property(e => e.ProcessingCompletedAtUtc).HasColumnName("ProcessamentoFimUtc");
            entity.Property(e => e.CorrelationId).HasColumnName("CorrelationId").HasMaxLength(60);
            entity.Property(e => e.RowVersion).HasColumnName("Versao").IsConcurrencyToken();
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.CompanyDb, e.FileHash }).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CompanyDb);
            entity.HasIndex(e => e.Status).HasDatabaseName("IX_ImportacaoArquivo_Status");
            entity.HasIndex(e => e.CorrelationId).HasDatabaseName("IX_ImportacaoArquivo_CorrelationId");
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
            entity.Property(e => e.GroupKeyHash).HasColumnName("HashChaveGrupo").HasMaxLength(64);
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("AtualizadoEmUtc");
            entity.HasOne(e => e.ImportFile).WithMany(f => f.Lines).HasForeignKey(e => e.ImportFileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.CompanyDb, e.BusinessKeyHash }).IsUnique();
            entity.HasIndex(e => e.ImportFileId);
            entity.HasIndex(e => e.Reference).HasDatabaseName("IX_ImportacaoLinha_Referencia");
            entity.HasIndex(e => new { e.ImportFileId, e.GroupKeyHash }).HasDatabaseName("IX_ImportacaoLinha_Grupo");
            entity.HasIndex(e => e.Status).HasDatabaseName("IX_ImportacaoLinha_Status");
        });

        modelBuilder.Entity<SystemLog>(entity =>
        {
            entity.ToTable("LogSistema");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.OccurredAt).HasColumnName("DataHora").IsRequired();
            entity.Property(e => e.Level).HasColumnName("Nivel").HasMaxLength(20).IsRequired();
            entity.Property(e => e.Category).HasColumnName("Categoria").HasMaxLength(30);
            entity.Property(e => e.Source).HasColumnName("Origem").HasMaxLength(120).IsRequired();
            entity.Property(e => e.Operation).HasColumnName("Operacao").HasMaxLength(120);
            entity.Property(e => e.UserId).HasColumnName("UsuarioId");
            entity.Property(e => e.CompanyDb).HasColumnName("CompanyDb").HasMaxLength(50);
            entity.Property(e => e.CorrelationId).HasColumnName("CorrelationId").HasMaxLength(60);
            entity.Property(e => e.CausationId).HasColumnName("CausationId").HasMaxLength(60);
            entity.Property(e => e.MessageId).HasColumnName("MessageId").HasMaxLength(60);
            entity.Property(e => e.SapSessionId).HasColumnName("SapSessionId").HasMaxLength(120);
            entity.Property(e => e.ImportFileId).HasColumnName("ImportacaoArquivoId");
            entity.Property(e => e.ImportLineId).HasColumnName("ImportacaoLinhaId");
            entity.Property(e => e.BusinessKey).HasColumnName("ChaveNegocio").HasMaxLength(200);
            entity.Property(e => e.StatusBefore).HasColumnName("StatusAntes").HasMaxLength(40);
            entity.Property(e => e.StatusAfter).HasColumnName("StatusDepois").HasMaxLength(40);
            entity.Property(e => e.DurationMs).HasColumnName("DuracaoMs");
            entity.Property(e => e.Message).HasColumnName("Mensagem").HasMaxLength(1024).IsRequired();
            entity.Property(e => e.Details).HasColumnName("Detalhes");
            entity.Property(e => e.StackTrace).HasColumnName("StackTrace");
            entity.Property(e => e.MachineName).HasColumnName("Hostname").HasMaxLength(120);
            entity.Property(e => e.Environment).HasColumnName("Ambiente").HasMaxLength(40);
            entity.Property(e => e.Application).HasColumnName("Aplicacao").HasMaxLength(80);
            entity.HasIndex(e => e.OccurredAt);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CorrelationId);
            entity.HasIndex(e => new { e.Category, e.Level }).HasDatabaseName("IX_LogSistema_Categoria_Nivel");
            entity.HasIndex(e => e.ImportFileId).HasDatabaseName("IX_LogSistema_ImportacaoArquivoId");
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

        // ===== System settings =====
        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.ToTable("ConfiguracaoSistema");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.Chave).HasColumnName("Chave").HasMaxLength(120).IsRequired();
            entity.Property(e => e.Valor).HasColumnName("Valor").HasColumnType("text");
            entity.Property(e => e.Categoria).HasColumnName("Categoria").HasMaxLength(60).IsRequired();
            entity.Property(e => e.Descricao).HasColumnName("Descricao").HasMaxLength(300);
            entity.Property(e => e.TipoDado).HasColumnName("TipoDado").HasMaxLength(20).HasDefaultValue("string").IsRequired();
            entity.Property(e => e.Obrigatorio).HasColumnName("Obrigatorio").IsRequired();
            entity.Property(e => e.AtualizadoEm).HasColumnName("AtualizadoEm");
            entity.Property(e => e.AtualizadoPor).HasColumnName("AtualizadoPor").HasMaxLength(80);
            entity.HasIndex(e => e.Chave).IsUnique().HasDatabaseName("UQ_ConfiguracaoSistema_Chave");
            entity.HasIndex(e => e.Categoria).HasDatabaseName("IX_ConfiguracaoSistema_Categoria");
        });

        // ===== Outbox (messaging) =====
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("MensagensOutbox");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.Channel).HasColumnName("Canal").HasMaxLength(120).IsRequired();
            entity.Property(e => e.MessageType).HasColumnName("TipoMensagem").HasMaxLength(300).IsRequired();
            entity.Property(e => e.MessageId).HasColumnName("MessageId").HasMaxLength(60).IsRequired();
            entity.Property(e => e.CorrelationId).HasColumnName("CorrelationId").HasMaxLength(60);
            entity.Property(e => e.CausationId).HasColumnName("CausationId").HasMaxLength(60);
            entity.Property(e => e.Payload).HasColumnName("Payload").HasColumnType("longtext").IsRequired();
            entity.Property(e => e.Broker).HasColumnName("Broker").HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasColumnName("Status").HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(e => e.CreatedAtUtc).HasColumnName("CriadoEmUtc").IsRequired();
            entity.Property(e => e.DispatchedAtUtc).HasColumnName("EnviadoEmUtc");
            entity.Property(e => e.NextAttemptAtUtc).HasColumnName("ProximaTentativaUtc");
            entity.Property(e => e.ClaimedUntilUtc).HasColumnName("ReservadoAteUtc");
            entity.Property(e => e.AttemptCount).HasColumnName("QuantidadeTentativas").IsRequired();
            entity.Property(e => e.LastError).HasColumnName("UltimoErro").HasMaxLength(2000);
            entity.Property(e => e.UserId).HasColumnName("UsuarioId");
            entity.Property(e => e.CompanyDb).HasColumnName("CompanyDb").HasMaxLength(50);
            entity.HasIndex(e => e.MessageId).IsUnique();
            entity.HasIndex(e => new { e.Status, e.NextAttemptAtUtc })
                .HasDatabaseName("IX_MensagensOutbox_Status_Proxima");
            entity.HasIndex(e => e.CorrelationId);
        });

        // ===== Inbox (idempotency) =====
        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("MensagensInbox");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.Consumer).HasColumnName("Consumidor").HasMaxLength(120).IsRequired();
            entity.Property(e => e.MessageId).HasColumnName("MessageId").HasMaxLength(60).IsRequired();
            entity.Property(e => e.ProcessedAtUtc).HasColumnName("ProcessadoEmUtc").IsRequired();
            entity.Property(e => e.CorrelationId).HasColumnName("CorrelationId").HasMaxLength(60);
            entity.HasIndex(e => new { e.Consumer, e.MessageId }).IsUnique();
            entity.HasIndex(e => e.CorrelationId);
        });

        // ===== JournalEntryDispatch (SAP dispatch tracking) =====
        modelBuilder.Entity<JournalEntryDispatch>(entity =>
        {
            entity.ToTable("LancamentoSapDispatch");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.ImportFileId).HasColumnName("ImportacaoArquivoId").IsRequired();
            entity.Property(e => e.CompanyDb).HasColumnName("CompanyDb").HasMaxLength(50).IsRequired();
            entity.Property(e => e.GroupKeyHash).HasColumnName("HashChaveGrupo").HasMaxLength(64).IsRequired();
            entity.Property(e => e.GroupKey).HasColumnName("ChaveGrupo").HasMaxLength(400).IsRequired();
            entity.Property(e => e.Status).HasColumnName("Status").HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(e => e.AttemptCount).HasColumnName("QuantidadeTentativas").IsRequired();
            entity.Property(e => e.CreatedAtUtc).HasColumnName("CriadoEmUtc").IsRequired();
            entity.Property(e => e.DispatchedAtUtc).HasColumnName("EnviadoEmUtc");
            entity.Property(e => e.LastAttemptAtUtc).HasColumnName("UltimaTentativaUtc");
            entity.Property(e => e.SapDocEntry).HasColumnName("DocEntrySap");
            entity.Property(e => e.SapResponseSummary).HasColumnName("RespostaSap").HasMaxLength(2000);
            entity.Property(e => e.LastError).HasColumnName("UltimoErro").HasMaxLength(2000);
            entity.Property(e => e.CorrelationId).HasColumnName("CorrelationId").HasMaxLength(60);
            entity.HasOne(e => e.ImportFile)
                .WithMany(f => f.Dispatches)
                .HasForeignKey(e => e.ImportFileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.CompanyDb, e.GroupKeyHash }).IsUnique();
            entity.HasIndex(e => e.Status).HasDatabaseName("IX_LancamentoSapDispatch_Status");
            entity.HasIndex(e => e.ImportFileId).HasDatabaseName("IX_LancamentoSapDispatch_Arquivo");
        });
    }

    /// <summary>
    /// Persists changes while automatically stamping audit timestamps
    /// and enforcing the hard column caps that MySQL would otherwise
    /// reject. Error translation is NOT done here anymore: letting
    /// EF Core throw <see cref="DbUpdateException"/> lets the
    /// application layer decide how to react (retry, surface, audit).
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        NormalizeEntities();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void StampTimestamps()
    {
        var nowUtc = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case ImportFile file when entry.State is EntityState.Added or EntityState.Modified:
                    file.UpdatedAtUtc = nowUtc;
                    break;
                case ImportLine line when entry.State is EntityState.Added or EntityState.Modified:
                    line.UpdatedAtUtc = nowUtc;
                    break;
                case OutboxMessage outbox when entry.State == EntityState.Added:
                    if (outbox.CreatedAtUtc == default) outbox.CreatedAtUtc = nowUtc;
                    break;
                case InboxMessage inbox when entry.State == EntityState.Added:
                    if (inbox.ProcessedAtUtc == default) inbox.ProcessedAtUtc = nowUtc;
                    break;
                case JournalEntryDispatch dispatch when entry.State == EntityState.Added:
                    if (dispatch.CreatedAtUtc == default) dispatch.CreatedAtUtc = nowUtc;
                    break;
                case SystemLog log when entry.State == EntityState.Added:
                    if (log.OccurredAt == default) log.OccurredAt = nowUtc;
                    break;
            }
        }
    }

    private void NormalizeEntities()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added && entry.State != EntityState.Modified)
                continue;

            if (entry.Entity is ImportLine line)
            {
                line.ValidationMessage  = Truncate(line.ValidationMessage, 400);
                line.SapReturnMessage   = Truncate(line.SapReturnMessage, 400);
                line.LineMemo           = Truncate(line.LineMemo, 200) ?? string.Empty;
                line.Reference          = Truncate(line.Reference, 120) ?? string.Empty;
            }
            else if (entry.Entity is ImportFile file)
            {
                file.OriginalFileName = Truncate(file.OriginalFileName, 200) ?? string.Empty;
            }
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength
            ? value
            : value.Substring(0, maxLength - 3) + "...";
    }
}