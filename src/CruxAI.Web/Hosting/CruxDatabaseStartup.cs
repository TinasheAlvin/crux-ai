using CruxAI.Infrastructure.Data;
using CruxAI.Infrastructure.Hosting;
using Microsoft.EntityFrameworkCore;

namespace CruxAI.Web.Hosting;

public static class CruxDatabaseStartup
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (!HostingConfiguration.IsAzureSql(configuration))
        {
            EnsureSqliteDirectory(configuration.GetConnectionString("Sqlite"));
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CruxDbContext>();
        try
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            await SqliteSchemaPatches.ApplyAsync(db, cancellationToken);
        }
        catch (Exception ex) when (HostingConfiguration.IsAzureSql(configuration) && ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Could not initialize Azure SQL. Check ConnectionStrings:AzureSql, the AllowAzureServices firewall rule, " +
                "and that the database exists. Schema is created on first startup with EF EnsureCreated.",
                ex);
        }
    }

    private static void EnsureSqliteDirectory(string? sqlitePath)
    {
        if (string.IsNullOrWhiteSpace(sqlitePath)
            || !sqlitePath.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var file = sqlitePath.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (file.Contains(':'))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
