using VhonaAI.Core.Entities;
using VhonaAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VhonaAI.Tests;

public class DatabaseMigrationTests
{
    [Fact]
    public async Task Fresh_sqlite_database_applies_every_migration()
    {
        await using var db = new VhonaDbContext(SqliteOptions());
        await db.Database.MigrateAsync();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Equal(3, applied.Count);
        Assert.Contains(applied, name => name.EndsWith("_AdminConsole", StringComparison.Ordinal));
        Assert.Equal(0, await db.Organizations.CountAsync());
        Assert.Equal(0, await db.Customers.CountAsync());
        Assert.Equal(0, await db.Invoices.CountAsync());
        Assert.Equal(0, await db.InvoiceLines.CountAsync());
        Assert.Equal(0, await db.Payments.CountAsync());
        Assert.Equal(0, await db.CreditNotes.CountAsync());
        Assert.Equal(0, await db.DataSources.CountAsync());
        Assert.Equal(0, await db.Invitations.CountAsync());
        Assert.Equal(0, await db.CallThresholdSettings.CountAsync());
        Assert.Equal(0, await db.AdminAuditEntries.CountAsync());
        Assert.Equal(0, await db.PlatformCallDefaults.CountAsync());
    }

    [Fact]
    public async Task Existing_baseline_database_is_stamped_and_keeps_its_rows()
    {
        var options = SqliteOptions();
        Guid orgId;
        await using (var db = new VhonaDbContext(options))
        {
            var migrations = db.Database.GetMigrations().ToList();
            Assert.Equal(3, migrations.Count);
            await db.GetService<IMigrator>().MigrateAsync(migrations[0]);

            orgId = Guid.NewGuid();
            var createdAt = DateTime.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Organizations" ("Id", "Name", "CreatedAt")
                VALUES ({orgId}, {"Kept Studio"}, {createdAt});
                """);
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"__EFMigrationsHistory\";");
        }

        await using (var db = new VhonaDbContext(options))
        {
            await VhonaDatabaseMigrator.ApplyAsync(db);

            var organization = await db.Organizations.SingleAsync();
            Assert.Equal(orgId, organization.Id);
            Assert.Equal("Kept Studio", organization.Name);
            Assert.Equal(0, await db.Customers.CountAsync());

            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.Equal(db.Database.GetMigrations().ToList(), applied);
        }
    }

    [Fact]
    public async Task Partial_old_database_is_not_stamped()
    {
        await using var db = new VhonaDbContext(SqliteOptions());
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE Organizations (
                Id TEXT NOT NULL CONSTRAINT PK_Organizations PRIMARY KEY,
                Name TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            """);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => VhonaDatabaseMigrator.ApplyAsync(db));
        Assert.Contains("missing tables", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Customers_without_history_are_not_stamped()
    {
        var options = SqliteOptions();
        await using (var db = new VhonaDbContext(options))
        {
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"__EFMigrationsHistory\";");
        }

        await using var again = new VhonaDbContext(options);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => VhonaDatabaseMigrator.ApplyAsync(again));
        Assert.Contains("Customers", ex.Message);
    }

    [Fact]
    public void SqlServer_script_creates_the_offer_tables()
    {
        var options = new DbContextOptionsBuilder<VhonaDbContext>()
            .UseSqlServer("Server=localhost;Database=vhonaai-script;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new VhonaDbContext(options);
        var script = db.GetService<IMigrator>().GenerateScript();

        Assert.Contains("CREATE TABLE [Organizations]", script);
        Assert.Contains("CREATE TABLE [Customers]", script);
        Assert.Contains("CREATE TABLE [Invoices]", script);
        Assert.Contains("CREATE TABLE [InvoiceLines]", script);
        Assert.Contains("CREATE TABLE [Payments]", script);
        Assert.Contains("CREATE TABLE [CreditNotes]", script);
        Assert.Contains("CREATE TABLE [DataSources]", script);
        Assert.Contains("CREATE TABLE [Invitations]", script);
        Assert.Contains("CREATE TABLE [CallThresholdSettings]", script);
        Assert.Contains("CREATE TABLE [AdminAuditEntries]", script);
        Assert.Contains("CREATE TABLE [PlatformCallDefaults]", script);
        Assert.Contains("[DisabledAt]", script);
        Assert.Contains("[BookedDate]", script);
        Assert.Contains("[Kind]", script);
    }

    [Fact]
    public void Each_table_has_at_most_one_cascade_path_from_organization()
    {
        var options = new DbContextOptionsBuilder<VhonaDbContext>()
            .UseSqlServer("Server=localhost;Database=vhonaai-model;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new VhonaDbContext(options);
        var organization = db.Model.FindEntityType(typeof(Organization))
            ?? throw new InvalidOperationException("Organization is not in the model.");

        foreach (var type in db.Model.GetEntityTypes().Where(type => !type.IsOwned()))
        {
            var paths = CascadePathsFrom(type, organization, new HashSet<IEntityType>());
            Assert.True(paths <= 1, $"{type.DisplayName()} has {paths} cascade paths from Organization.");
        }
    }

    private static int CascadePathsFrom(IEntityType type, IEntityType organization, HashSet<IEntityType> stack)
    {
        if (type == organization)
        {
            return 1;
        }

        if (!stack.Add(type))
        {
            return 0;
        }

        var paths = type.GetForeignKeys()
            .Where(fk => fk.DeleteBehavior == DeleteBehavior.Cascade)
            .Sum(fk => CascadePathsFrom(fk.PrincipalEntityType, organization, stack));
        stack.Remove(type);
        return paths;
    }

    private static DbContextOptions<VhonaDbContext> SqliteOptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "vhonaai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new DbContextOptionsBuilder<VhonaDbContext>()
            .UseVhonaSqlite($"Data Source={Path.Combine(root, "test.db")}")
            .Options;
    }
}
