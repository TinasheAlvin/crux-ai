using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VhonaAI.Infrastructure.Data;

/// <summary>
/// Applies EF migrations on SQLite and Azure SQL.
/// A database created by the old EnsureCreated startup has no history table.
/// When it already matches the baseline schema, that migration is stamped and only later migrations run,
/// so existing rows are kept.
/// </summary>
public static class VhonaDatabaseMigrator
{
    public const string EfProductVersion = "8.0.21";

    public static async Task ApplyAsync(VhonaDbContext db, CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            if (applied.Count == 0 && await IsPreMigrationDatabaseAsync(db, cancellationToken))
            {
                await StampBaselineAsync(db, cancellationToken);
            }
        }
        finally
        {
            if (connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }

        await db.Database.MigrateAsync(cancellationToken);
    }

    private static async Task<bool> IsPreMigrationDatabaseAsync(VhonaDbContext db, CancellationToken cancellationToken)
    {
        var hasOrganizations = await TableExistsAsync(db, "Organizations", cancellationToken);
        if (!hasOrganizations)
        {
            return false;
        }

        var hasCustomers = await TableExistsAsync(db, "Customers", cancellationToken);
        if (hasCustomers)
        {
            throw new InvalidOperationException(
                "The database already has Customers but no EF migration history. " +
                "Refusing to guess which migrations ran. Restore a backup or stamp __EFMigrationsHistory by hand.");
        }

        var hasWhyAnswers = await TableExistsAsync(db, "WhyAnswers", cancellationToken);
        var hasMorningBriefs = await TableExistsAsync(db, "MorningBriefs", cancellationToken);
        if (!hasWhyAnswers || !hasMorningBriefs)
        {
            throw new InvalidOperationException(
                "The database has Organizations but is missing tables from the current Vhona schema. " +
                "Refusing to stamp the baseline migration. Backup the database before changing it.");
        }

        return true;
    }

    private static async Task StampBaselineAsync(VhonaDbContext db, CancellationToken cancellationToken)
    {
        var baseline = db.Database.GetMigrations().FirstOrDefault()
            ?? throw new InvalidOperationException("No EF migrations are compiled into the app.");

        var history = db.GetService<IHistoryRepository>();
        if (!history.Exists())
        {
            await db.Database.ExecuteSqlRawAsync(history.GetCreateScript(), cancellationToken);
        }

        var applied = history.GetAppliedMigrations().Select(row => row.MigrationId).ToHashSet(StringComparer.Ordinal);
        if (applied.Contains(baseline))
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            history.GetInsertScript(new HistoryRow(baseline, EfProductVersion)),
            cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(
        VhonaDbContext db,
        string table,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        if (db.Database.IsSqlite())
        {
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$name";
            parameter.Value = table;
            command.Parameters.Add(parameter);
        }
        else
        {
            command.CommandText = "SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @name;";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@name";
            parameter.Value = table;
            command.Parameters.Add(parameter);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null and not DBNull;
    }
}
