using Microsoft.EntityFrameworkCore;

namespace CruxAI.Infrastructure.Data;

public static class SqliteSchemaPatches
{
    public static async Task ApplyAsync(CruxDbContext db, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsSqlite())
        {
            return;
        }

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('Transactions');";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (columns.Count > 0 && !columns.Contains("Balance"))
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Transactions ADD COLUMN Balance TEXT;", cancellationToken);
        }
    }
}
