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

        // Reconcile individual base columns that legacy databases (created outside EF,
        // or from an older dump) may be missing but the current model maps. These are
        // additive, nullable, and idempotent — safe to run on every startup, and they
        // run against the SAME connection the seeder uses, so whatever database the app
        // is actually pointed at gets the column. This fixes the seeder crashing with
        // "Unknown column 'Grupo' in 'field list'" when Permissoes predates the column.
        await EnsureBaseColumnsAsync(connection, logger, cancellationToken);

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

    // Base-table columns the current model maps that some legacy databases lack.
    // (table, column, DDL definition). Kept additive and nullable so applying them
    // can never destroy data or conflict with existing rows.
    private static readonly (string Table, string Column, string Definition)[] RequiredBaseColumns =
    {
        // Permissoes: full column set the Permission entity maps. Legacy databases
        // may be missing any of the non-key columns (Grupo, Ativo were both absent
        // on at least one production DB). Added idempotently and safely for existing
        // rows: text columns nullable, the required bool with a NOT NULL default.
        ("Permissoes", "Codigo", "varchar(80) NULL"),
        ("Permissoes", "Nome", "varchar(120) NULL"),
        ("Permissoes", "Descricao", "varchar(200) NULL"),
        ("Permissoes", "Grupo", "varchar(80) NULL"),
        ("Permissoes", "Ativo", "tinyint(1) NOT NULL DEFAULT 1"),
    };

    private static async Task EnsureBaseColumnsAsync(DbConnection c, ILogger logger, CancellationToken ct)
    {
        foreach (var (table, column, definition) in RequiredBaseColumns)
        {
            if (!await TableExistsAsync(c, table, ct))
                continue;
            if (await ColumnExistsAsync(c, table, column, ct))
                continue;

            await ExecuteAsync(c, $"ALTER TABLE `{table}` ADD COLUMN `{column}` {definition};", ct);
            logger.LogWarning(
                "Baseline: coluna ausente '{Table}.{Column}' criada ({Definition}) para alinhar o schema ao modelo.",
                table, column, definition);
        }
    }

    private static async Task<bool> ColumnExistsAsync(DbConnection c, string table, string column, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM information_schema.COLUMNS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND COLUMN_NAME = @c;";
        AddParam(cmd, "@t", table);
        AddParam(cmd, "@c", column);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
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
