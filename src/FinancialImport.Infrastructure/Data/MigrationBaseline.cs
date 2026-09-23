using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialImport.Infrastructure.Data;

/// <summary>
/// Startup self-heal for the classic "schema exists but __EFMigrationsHistory is
/// empty" situation (DB provisioned outside EF, restored from a dump, or left in a
/// half-applied state because MySQL does not roll back DDL). In that state EF lists
/// every migration as pending and dies on "Table 'usuarios' already exists".
///
/// This routine runs BEFORE MigrateAsync. When it detects that the base schema
/// already exists (the <c>Usuarios</c> table is present) but the history does not
/// record <c>InitialCreate</c>, it baselines the history for the 9 base migrations
/// and reconciles the (data-less) settlement tables so EF can apply just the
/// remaining settlement migrations cleanly. It NEVER drops a table that has rows,
/// and NEVER touches base/business tables.
/// </summary>
public static class MigrationBaseline
{
    private const string ProductVersion = "9.0.0";

    private static readonly string[] BaseMigrations =
    {
        "20260407000000_InitialCreate",
        "20260408000000_AddMissingFkIndexes",
        "20260408162944_AddReferenciaIndex",
        "20260409000000_AddMessagingAndRichLogs",
        "20260409000001_AddSystemSettings",
        "20260507000000_AddCostingCodeToImportLine",
        "20260528000000_ChangeImportLineUniqueIndexToPerFile",
        "20260528000001_ChangeDispatchUniqueIndexToPerFile",
        "20260608000000_AddImportLineFlagsCoveringIndex",
    };

    private static readonly string[] SettlementMigrations =
    {
        "20260622000000_AddReceivableSettlement",
        "20260622000001_SettlementMultiplePaymentsPerInvoice",
        "20260622000002_DropCardBrandMapping",
        "20260622000003_AddSettlementDocumentDate",
    };

    // Settlement tables that migration AddReceivableSettlement creates. They must
    // be absent for EF to re-create them; they hold no business data until the
    // module actually runs, so an EMPTY one is safe to drop. Children first (FKs).
    private static readonly string[] SettlementTables =
    {
        "BaixaLinha",
        "BaixaSapDispatch",
        "BaixaArquivo",
        "MapeamentoBandeiraCartao",
    };

    public static async Task EnsureBaselineAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        // Fresh database (no base schema yet): nothing to baseline — let EF create everything.
        if (!await TableExistsAsync(connection, "Usuarios", cancellationToken))
            return;

        // Recreate base tables the seeder needs that may have been dropped manually
        // while the migration history still marks InitialCreate as applied (so EF will
        // NOT recreate them). Idempotent via CREATE TABLE IF NOT EXISTS. FKs to Usuarios
        // are intentionally omitted to avoid the bigint-vs-bigint-unsigned mismatch some
        // legacy Usuarios tables have; EF does not require DB-level FKs to operate.
        await EnsureBaseTablesAsync(connection, logger, cancellationToken);

        // Reconcile individual base columns that legacy databases (created outside EF,
        // or from an older dump) may be missing but the current model maps. These are
        // additive, nullable, and idempotent — safe to run on every startup, and they
        // run against the SAME connection the seeder uses, so whatever database the app
        // is actually pointed at gets the column. This fixes the seeder crashing with
        // "Unknown column 'Grupo' in 'field list'" when Permissoes predates the column.
        await EnsureBaseColumnsAsync(connection, logger, cancellationToken);

        // Legacy/foreign schemas may carry EXTRA columns the current model does not map
        // (e.g. a 'Modulo' column on Permissoes from an older product version). If such a
        // column is NOT NULL without a default, the seeder's INSERT — which never mentions
        // it — fails with "Field '<x>' doesn't have a default value". Relax every such
        // unknown required column to NULL so the omitted-column insert succeeds. Only
        // columns absent from the model are touched; real model columns keep their shape.
        await RelaxUnknownRequiredColumnsAsync(connection, logger, cancellationToken);

        await EnsureHistoryTableAsync(connection, cancellationToken);

        // If InitialCreate is already tracked, the history is healthy — normal flow.
        if (await HistoryHasAsync(connection, BaseMigrations[0], cancellationToken))
            return;

        logger.LogWarning(
            "Schema base detectado SEM histórico de migrations (__EFMigrationsHistory sem InitialCreate). " +
            "Aplicando baseline automático para reconciliar sem perder dados.");

        // 1) Reconcile settlement tables so the 4 settlement migrations can re-apply.
        var settlementHasData = false;
        foreach (var table in SettlementTables)
        {
            if (!await TableExistsAsync(connection, table, cancellationToken))
                continue;

            var rows = await RowCountAsync(connection, table, cancellationToken);
            if (rows == 0)
            {
                await ExecuteAsync(connection, "SET FOREIGN_KEY_CHECKS = 0;", cancellationToken);
                await ExecuteAsync(connection, $"DROP TABLE IF EXISTS `{table}`;", cancellationToken);
                await ExecuteAsync(connection, "SET FOREIGN_KEY_CHECKS = 1;", cancellationToken);
                logger.LogInformation("Baseline: tabela vazia '{Table}' removida para recriação pelas migrations.", table);
            }
            else
            {
                settlementHasData = true;
                logger.LogWarning("Baseline: tabela '{Table}' possui {Rows} registro(s) — não será removida.", table, rows);
            }
        }

        // 2) Mark the 9 base migrations as applied (their schema already exists).
        foreach (var id in BaseMigrations)
            await InsertHistoryAsync(connection, id, cancellationToken);

        // 3) If any settlement table already held data, assume the settlement schema
        //    was fully applied before and mark those migrations applied too (avoid
        //    trying to recreate populated tables). Otherwise leave them pending so
        //    EF applies them fresh.
        if (settlementHasData)
        {
            foreach (var id in SettlementMigrations)
                await InsertHistoryAsync(connection, id, cancellationToken);
            logger.LogWarning("Baseline: migrations da Baixa marcadas como aplicadas (havia dados). Verifique o schema manualmente se necessário.");
        }

        logger.LogInformation("Baseline concluído. As migrations restantes serão aplicadas a seguir.");
    }

    // Full non-key column spec for the permission-cluster base tables, mirroring the
    // EF model (AppDbContext). Each entry: table, column, SQL type, and whether the
    // MODEL treats it as nullable. Reconciliation is twofold and always data-safe:
    //   • column missing        → ADD it (text as NULL; bool/bigint NOT NULL DEFAULT);
    //   • column NOT NULL in DB  → but nullable in the model → MODIFY to NULL (relax).
    // We never tighten NULL→NOT NULL and never change types, so no existing row can
    // conflict. The relax step fixes the seeder crashing with "Column '<x>' cannot be
    // null" when a legacy table declared an optional column (Descricao/Grupo) NOT NULL.
    private static readonly (string Table, string Column, string SqlType, bool ModelNullable)[] BaseColumnSpecs =
    {
        ("Perfis",                  "Nome",       "varchar(80)",  false),
        ("Perfis",                  "Descricao",  "varchar(200)", true),
        ("Perfis",                  "Ativo",      "tinyint(1)",   false),

        ("Permissoes",              "Codigo",     "varchar(80)",  false),
        ("Permissoes",              "Nome",       "varchar(120)", false),
        ("Permissoes",              "Descricao",  "varchar(200)", true),
        ("Permissoes",              "Grupo",      "varchar(80)",  true),
        ("Permissoes",              "Ativo",      "tinyint(1)",   false),

        ("UsuarioPerfil",           "UsuarioId",  "bigint",       false),
        ("UsuarioPerfil",           "PerfilId",   "bigint",       false),

        ("PerfilPermissao",         "PerfilId",   "bigint",       false),
        ("PerfilPermissao",         "PermissaoId","bigint",       false),

        ("UsuarioEmpresaPermitida", "UsuarioId",  "bigint",       false),
        ("UsuarioEmpresaPermitida", "CompanyDb",  "varchar(50)",  false),
        ("UsuarioEmpresaPermitida", "Ativo",      "tinyint(1)",   false),
    };

    // Base tables the seeder depends on, with the DDL to recreate them if they were
    // dropped. Order matters only for readability — no FKs are declared so they can be
    // created in any order. Columns/types/indexes mirror the InitialCreate migration.
    private static readonly (string Table, string CreateSql)[] RequiredBaseTables =
    {
        ("Perfis",
            "CREATE TABLE IF NOT EXISTS `Perfis` (" +
            "`Id` bigint NOT NULL AUTO_INCREMENT, " +
            "`Nome` varchar(80) NOT NULL, " +
            "`Descricao` varchar(200) NULL, " +
            "`Ativo` tinyint(1) NOT NULL DEFAULT 1, " +
            "CONSTRAINT `PK_Perfis` PRIMARY KEY (`Id`), " +
            "UNIQUE KEY `IX_Perfis_Nome` (`Nome`)) CHARACTER SET=utf8mb4;"),

        ("Permissoes",
            "CREATE TABLE IF NOT EXISTS `Permissoes` (" +
            "`Id` bigint NOT NULL AUTO_INCREMENT, " +
            "`Codigo` varchar(80) NOT NULL, " +
            "`Nome` varchar(120) NOT NULL, " +
            "`Descricao` varchar(200) NULL, " +
            "`Grupo` varchar(80) NULL, " +
            "`Ativo` tinyint(1) NOT NULL DEFAULT 1, " +
            "CONSTRAINT `PK_Permissoes` PRIMARY KEY (`Id`), " +
            "UNIQUE KEY `IX_Permissoes_Codigo` (`Codigo`)) CHARACTER SET=utf8mb4;"),

        ("UsuarioPerfil",
            "CREATE TABLE IF NOT EXISTS `UsuarioPerfil` (" +
            "`Id` bigint NOT NULL AUTO_INCREMENT, " +
            "`UsuarioId` bigint NOT NULL, " +
            "`PerfilId` bigint NOT NULL, " +
            "CONSTRAINT `PK_UsuarioPerfil` PRIMARY KEY (`Id`), " +
            "UNIQUE KEY `IX_UsuarioPerfil_UsuarioId_PerfilId` (`UsuarioId`, `PerfilId`)) CHARACTER SET=utf8mb4;"),

        ("PerfilPermissao",
            "CREATE TABLE IF NOT EXISTS `PerfilPermissao` (" +
            "`Id` bigint NOT NULL AUTO_INCREMENT, " +
            "`PerfilId` bigint NOT NULL, " +
            "`PermissaoId` bigint NOT NULL, " +
            "CONSTRAINT `PK_PerfilPermissao` PRIMARY KEY (`Id`), " +
            "UNIQUE KEY `IX_PerfilPermissao_PerfilId_PermissaoId` (`PerfilId`, `PermissaoId`)) CHARACTER SET=utf8mb4;"),

        ("UsuarioEmpresaPermitida",
            "CREATE TABLE IF NOT EXISTS `UsuarioEmpresaPermitida` (" +
            "`Id` bigint NOT NULL AUTO_INCREMENT, " +
            "`UsuarioId` bigint NOT NULL, " +
            "`CompanyDb` varchar(50) NOT NULL, " +
            "`Ativo` tinyint(1) NOT NULL DEFAULT 1, " +
            "CONSTRAINT `PK_UsuarioEmpresaPermitida` PRIMARY KEY (`Id`), " +
            "UNIQUE KEY `IX_UsuarioEmpresaPermitida_UsuarioId_CompanyDb` (`UsuarioId`, `CompanyDb`)) CHARACTER SET=utf8mb4;"),
    };

    private static async Task EnsureBaseTablesAsync(DbConnection c, ILogger logger, CancellationToken ct)
    {
        foreach (var (table, createSql) in RequiredBaseTables)
        {
            if (await TableExistsAsync(c, table, ct))
                continue;

            await ExecuteAsync(c, createSql, ct);
            logger.LogWarning(
                "Baseline: tabela ausente '{Table}' recriada (o seeder depende dela; o histórico já marcava InitialCreate como aplicado).",
                table);
        }
    }

    private static async Task EnsureBaseColumnsAsync(DbConnection c, ILogger logger, CancellationToken ct)
    {
        foreach (var (table, column, sqlType, modelNullable) in BaseColumnSpecs)
        {
            if (!await TableExistsAsync(c, table, ct))
                continue;

            // null = column absent; true = exists & nullable; false = exists & NOT NULL.
            var isNullable = await GetColumnNullabilityAsync(c, table, column, ct);

            if (isNullable is null)
            {
                // Missing column. Add it in a way that is always safe for existing rows
                // and any unique index: text columns as NULL (multiple NULLs are allowed,
                // and the seeder supplies real values on insert); numeric/bool columns
                // NOT NULL with a default so pre-existing rows get a value.
                var addClause = sqlType.StartsWith("varchar", StringComparison.OrdinalIgnoreCase)
                    ? "NULL"
                    : sqlType.StartsWith("tinyint", StringComparison.OrdinalIgnoreCase)
                        ? "NOT NULL DEFAULT 1"
                        : "NOT NULL DEFAULT 0";
                await ExecuteAsync(c, $"ALTER TABLE `{table}` ADD COLUMN `{column}` {sqlType} {addClause};", ct);
                logger.LogWarning(
                    "Baseline: coluna ausente '{Table}.{Column}' criada ({Type} {Clause}) para alinhar ao modelo.",
                    table, column, sqlType, addClause);
            }
            else if (modelNullable && isNullable == false)
            {
                // Column is stricter than the model (NOT NULL where the model allows NULL).
                // The seeder omits it and sends NULL, so relax it. Relaxing never conflicts
                // with existing data.
                await ExecuteAsync(c, $"ALTER TABLE `{table}` MODIFY COLUMN `{column}` {sqlType} NULL;", ct);
                logger.LogWarning(
                    "Baseline: coluna '{Table}.{Column}' estava NOT NULL mas o modelo permite NULL — relaxada para NULL.",
                    table, column);
            }
        }
    }

    /// <summary>
    /// For every permission-cluster table, relaxes to NULL any column that is NOT NULL,
    /// has no default, is not an auto-increment key, and is NOT part of the EF model.
    /// Such "orphan" required columns (left over from an older/foreign schema) otherwise
    /// break the seeder's INSERT, which only lists model columns. Relaxing NOT NULL to
    /// NULL never conflicts with existing data.
    /// </summary>
    private static async Task RelaxUnknownRequiredColumnsAsync(DbConnection c, ILogger logger, CancellationToken ct)
    {
        foreach (var table in BaseColumnSpecs.Select(s => s.Table).Distinct())
        {
            if (!await TableExistsAsync(c, table, ct))
                continue;

            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Id" };
            foreach (var spec in BaseColumnSpecs.Where(s => s.Table == table))
                known.Add(spec.Column);

            // Collect first (a reader must be closed before issuing ALTER on the same connection).
            var orphans = new List<(string Name, string Type)>();
            await using (var cmd = c.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT COLUMN_NAME, COLUMN_TYPE FROM information_schema.COLUMNS " +
                    "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t " +
                    "AND IS_NULLABLE = 'NO' AND COLUMN_DEFAULT IS NULL " +
                    "AND EXTRA NOT LIKE '%auto_increment%';";
                AddParam(cmd, "@t", table);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var name = reader.GetString(0);
                    if (!known.Contains(name))
                        orphans.Add((name, reader.GetString(1)));
                }
            }

            foreach (var (name, type) in orphans)
            {
                await ExecuteAsync(c, $"ALTER TABLE `{table}` MODIFY COLUMN `{name}` {type} NULL;", ct);
                logger.LogWarning(
                    "Baseline: coluna '{Table}.{Column}' é NOT NULL sem default e não pertence ao modelo — relaxada para NULL para não bloquear o seeder.",
                    table, name);
            }
        }
    }

    /// <summary>
    /// Returns null when the column does not exist, true when it exists and is nullable,
    /// false when it exists and is NOT NULL.
    /// </summary>
    private static async Task<bool?> GetColumnNullabilityAsync(DbConnection c, string table, string column, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText =
            "SELECT IS_NULLABLE FROM information_schema.COLUMNS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND COLUMN_NAME = @c;";
        AddParam(cmd, "@t", table);
        AddParam(cmd, "@c", column);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull)
            return null;
        return string.Equals(result.ToString(), "YES", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> TableExistsAsync(DbConnection c, string table, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t;";
        AddParam(cmd, "@t", table);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task<bool> HistoryHasAsync(DbConnection c, string migrationId, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM `__EFMigrationsHistory` WHERE `MigrationId` = @m;";
        AddParam(cmd, "@m", migrationId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task<long> RowCountAsync(DbConnection c, string table, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM `{table}`;";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    private static async Task InsertHistoryAsync(DbConnection c, string migrationId, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText =
            "INSERT IGNORE INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`) VALUES (@m, @v);";
        AddParam(cmd, "@m", migrationId);
        AddParam(cmd, "@v", ProductVersion);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnsureHistoryTableAsync(DbConnection c, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (" +
            "`MigrationId` varchar(150) NOT NULL, `ProductVersion` varchar(32) NOT NULL, " +
            "CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)) CHARACTER SET=utf8mb4;";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ExecuteAsync(DbConnection c, string sql, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddParam(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
