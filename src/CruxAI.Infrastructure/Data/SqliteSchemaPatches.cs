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

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS WhyAnswers (
                Id TEXT NOT NULL CONSTRAINT PK_WhyAnswers PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                AskedByUserId TEXT NULL,
                Question TEXT NOT NULL,
                Answer TEXT NOT NULL,
                Verified INTEGER NOT NULL,
                Metric TEXT NULL,
                CreatedAt TEXT NOT NULL,
                CONSTRAINT FK_WhyAnswers_Organizations_OrganizationId
                    FOREIGN KEY (OrganizationId) REFERENCES Organizations (Id) ON DELETE CASCADE
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_WhyAnswers_OrganizationId ON WhyAnswers (OrganizationId);
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_WhyAnswers_CreatedAt ON WhyAnswers (CreatedAt);
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS WhyCitations (
                Id TEXT NOT NULL CONSTRAINT PK_WhyCitations PRIMARY KEY,
                WhyAnswerId TEXT NOT NULL,
                RowId TEXT NOT NULL,
                Columns TEXT NOT NULL,
                PeriodLabel TEXT NULL,
                CONSTRAINT FK_WhyCitations_WhyAnswers_WhyAnswerId
                    FOREIGN KEY (WhyAnswerId) REFERENCES WhyAnswers (Id) ON DELETE CASCADE
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_WhyCitations_WhyAnswerId_RowId
                ON WhyCitations (WhyAnswerId, RowId);
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS MorningBriefPreferences (
                Id TEXT NOT NULL CONSTRAINT PK_MorningBriefPreferences PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                OptedIn INTEGER NOT NULL,
                Dismissed INTEGER NOT NULL,
                OptedInAt TEXT NULL,
                DismissedAt TEXT NULL,
                CONSTRAINT FK_MorningBriefPreferences_Organizations_OrganizationId
                    FOREIGN KEY (OrganizationId) REFERENCES Organizations (Id) ON DELETE CASCADE
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_MorningBriefPreferences_OrganizationId_UserId
                ON MorningBriefPreferences (OrganizationId, UserId);
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS MorningBriefs (
                Id TEXT NOT NULL CONSTRAINT PK_MorningBriefs PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                BriefDate TEXT NOT NULL,
                SnapshotJson TEXT NOT NULL,
                CurrentPeriodLabel TEXT NULL,
                PreviousPeriodLabel TEXT NULL,
                Explanation TEXT NOT NULL,
                Verified INTEGER NOT NULL,
                Metric TEXT NULL,
                WhyAnswerId TEXT NULL,
                CreatedAt TEXT NOT NULL,
                CONSTRAINT FK_MorningBriefs_Organizations_OrganizationId
                    FOREIGN KEY (OrganizationId) REFERENCES Organizations (Id) ON DELETE CASCADE,
                CONSTRAINT FK_MorningBriefs_WhyAnswers_WhyAnswerId
                    FOREIGN KEY (WhyAnswerId) REFERENCES WhyAnswers (Id) ON DELETE SET NULL
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_MorningBriefs_OrganizationId_UserId_BriefDate
                ON MorningBriefs (OrganizationId, UserId, BriefDate);
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS MorningBriefCitations (
                Id TEXT NOT NULL CONSTRAINT PK_MorningBriefCitations PRIMARY KEY,
                MorningBriefId TEXT NOT NULL,
                RowId TEXT NOT NULL,
                Columns TEXT NOT NULL,
                PeriodLabel TEXT NULL,
                CONSTRAINT FK_MorningBriefCitations_MorningBriefs_MorningBriefId
                    FOREIGN KEY (MorningBriefId) REFERENCES MorningBriefs (Id) ON DELETE CASCADE
            );
            """,
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_MorningBriefCitations_MorningBriefId_RowId
                ON MorningBriefCitations (MorningBriefId, RowId);
            """,
            cancellationToken);
    }
}
