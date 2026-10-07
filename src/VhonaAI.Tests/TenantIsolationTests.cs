using VhonaAI.Core.Entities;
using VhonaAI.Core.Identity;
using VhonaAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Tests;

public class TenantIsolationTests
{
    [Fact]
    public async Task One_business_cannot_read_another_business_rows()
    {
        var options = SqliteOptions();
        var studio = Guid.NewGuid();
        var bakery = Guid.NewGuid();
        await using (var db = new VhonaDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            Seed(db, studio, "Harbour Street Studio", "cust_studio");
            Seed(db, bakery, "Market Bakery", "cust_bakery");
            db.WhyAnswers.Add(Answer(studio, "why-studio"));
            db.WhyAnswers.Add(Answer(bakery, "why-bakery"));
            await db.SaveChangesAsync();

            db.WhyCitations.Add(new WhyCitation
            {
                Id = Guid.NewGuid(),
                WhyAnswerId = db.WhyAnswers.Local.Single(item => item.Question == "why-studio").Id,
                RowId = "imp_studio",
                Columns = "Amount"
            });
            db.WhyCitations.Add(new WhyCitation
            {
                Id = Guid.NewGuid(),
                WhyAnswerId = db.WhyAnswers.Local.Single(item => item.Question == "why-bakery").Id,
                RowId = "imp_bakery",
                Columns = "Amount"
            });
            await db.SaveChangesAsync();
        }

        await using var studioDb = new VhonaDbContext(options, new FixedUser(studio));
        Assert.Equal(new[] { "Harbour Street Studio" }, await studioDb.Organizations.Select(item => item.Name).ToListAsync());
        Assert.Equal(new[] { "cust_studio" }, await studioDb.Customers.Select(item => item.RowId).ToListAsync());
        Assert.Equal(new[] { "txn_studio" }, await studioDb.Transactions.Select(item => item.RowId).ToListAsync());
        Assert.Equal(new[] { "why-studio" }, await studioDb.WhyAnswers.Select(item => item.Question).ToListAsync());
        Assert.Equal(new[] { "imp_studio" }, await studioDb.WhyCitations.Select(item => item.RowId).ToListAsync());

        await using var bakeryDb = new VhonaDbContext(options, new FixedUser(bakery));
        Assert.Equal(new[] { "cust_bakery" }, await bakeryDb.Customers.Select(item => item.RowId).ToListAsync());
        Assert.DoesNotContain(await bakeryDb.Transactions.Select(item => item.RowId).ToListAsync(), row => row == "txn_studio");
    }

    [Fact]
    public async Task Save_refuses_a_row_for_another_business()
    {
        var options = SqliteOptions();
        var studio = Guid.NewGuid();
        var bakery = Guid.NewGuid();
        await using (var db = new VhonaDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            Seed(db, studio, "Harbour Street Studio", "cust_studio");
            Seed(db, bakery, "Market Bakery", "cust_bakery");
            await db.SaveChangesAsync();
        }

        await using var studioDb = new VhonaDbContext(options, new FixedUser(studio));
        var other = await studioDb.Customers.IgnoreQueryFilters().SingleAsync(item => item.RowId == "cust_bakery");
        other.Phone = "0820000000";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => studioDb.SaveChangesAsync());
        Assert.Contains("different business", ex.Message);

        studioDb.ChangeTracker.Clear();
        studioDb.Customers.Add(new Customer
        {
            Id = Guid.NewGuid(),
            OrganizationId = bakery,
            RowId = "cust_smuggled",
            Name = "Smuggled",
            NormalizedName = "SMUGGLED",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => studioDb.SaveChangesAsync());
    }

    [Fact]
    public async Task Unscoped_context_still_sees_every_business()
    {
        var options = SqliteOptions();
        await using var db = new VhonaDbContext(options);
        await db.Database.EnsureCreatedAsync();
        Seed(db, Guid.NewGuid(), "Harbour Street Studio", "cust_studio");
        Seed(db, Guid.NewGuid(), "Market Bakery", "cust_bakery");
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Customers.CountAsync());
        Assert.Equal(2, await db.Transactions.CountAsync());
    }

    private static void Seed(VhonaDbContext db, Guid organizationId, string name, string customerRowId)
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        db.Organizations.Add(new Organization { Id = organizationId, Name = name, CreatedAt = now });
        db.Users.Add(new AppUser
        {
            Id = userId,
            Email = $"{organizationId:N}@example.com",
            DisplayName = name,
            ExternalId = organizationId.ToString("N"),
            CreatedAt = now
        });
        db.ImportJobs.Add(new ImportJob
        {
            Id = jobId,
            OrganizationId = organizationId,
            CreatedByUserId = userId,
            OriginalFileName = "bank.csv",
            StoragePath = $"{organizationId:N}/bank.csv",
            Status = ImportStatus.Imported,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Customers.Add(new Customer
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            RowId = customerRowId,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ImportJobId = jobId,
            RowId = customerRowId.Replace("cust", "txn", StringComparison.Ordinal),
            SourceRowNumber = 2,
            Date = new DateOnly(2026, 3, 2),
            Description = "Cut",
            Amount = -100m,
            Currency = "ZAR",
            ImportedAt = now
        });
    }

    private static WhyAnswer Answer(Guid organizationId, string question) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Question = question,
            Answer = "Cited.",
            Verified = true,
            CreatedAt = DateTime.UtcNow
        };

    private static DbContextOptions<VhonaDbContext> SqliteOptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "vhonaai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new DbContextOptionsBuilder<VhonaDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, "test.db")}")
            .Options;
    }

    private sealed class FixedUser : ICurrentUser
    {
        public FixedUser(Guid organizationId) => OrganizationId = organizationId;

        public bool IsAuthenticated => true;
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid OrganizationId { get; }
        public string Email => "owner@example.com";
        public string DisplayName => "Owner";
        public string OrganizationName => "Business";
        public string Role => "Owner";
    }
}
