using VhonaAI.Core.Entities;
using VhonaAI.Core.Health;
using VhonaAI.Core.Identity;
using VhonaAI.Core.Why;
using VhonaAI.Infrastructure.Data;
using VhonaAI.Infrastructure.Health;
using VhonaAI.Infrastructure.Why;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Tests;

public class WhyServiceTests
{
    [Fact]
    public async Task Ask_persists_a_cited_answer_and_receipt_returns_those_rows()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Db.Transactions.AddRange(
            Txn(harness, "r-feb-in", new DateOnly(2026, 2, 14), 4200m, "Card machine settlement"),
            Txn(harness, "r-feb-retail", new DateOnly(2026, 2, 21), 650m, "Retail product sale"),
            Txn(harness, "r-feb-out", new DateOnly(2026, 2, 10), -1400m, "Colour"),
            Txn(harness, "r-mar-in", new DateOnly(2026, 3, 5), 3200m, "Card machine settlement"),
            Txn(harness, "r-mar-out", new DateOnly(2026, 3, 3), -2450m, "Wella professional stock"));
        await harness.Db.SaveChangesAsync();

        var asked = await harness.Why.AskAsync("Why did revenue change?");

        Assert.True(asked.Verified);
        Assert.Equal(HealthMetricKind.Revenue, asked.Metric);
        Assert.Equal(new[] { "r-mar-in", "r-feb-in", "r-feb-retail" }.OrderBy(x => x), asked.CitedRowIds.OrderBy(x => x));
        Assert.DoesNotContain("r-mar-out", asked.CitedRowIds);

        var log = await harness.Db.WhyAnswers.Include(a => a.Citations).SingleAsync();
        Assert.Equal(harness.OrgId, log.OrganizationId);
        Assert.Equal(asked.Question, log.Question);
        Assert.Equal(asked.Answer, log.Answer);
        Assert.True(log.Verified);
        Assert.NotEqual(default, log.CreatedAt);
        Assert.Equal(asked.CitedRowIds.OrderBy(x => x), log.Citations.Select(c => c.RowId).OrderBy(x => x));

        var receipt = await harness.Why.GetReceiptRowsAsync(asked.AnswerId);
        Assert.Equal(asked.CitedRowIds.OrderBy(x => x), receipt.Select(r => r.RowId).OrderBy(x => x));
        Assert.Contains(receipt, row => row.RowId == "r-mar-in" && row.Amount == 3200m && row.Description.Contains("settlement"));
        Assert.All(receipt, row => Assert.Contains("Amount", row.Columns));
        Assert.Equal(asked.CitedRows.Count, receipt.Count);
    }

    [Fact]
    public async Task Ask_fail_closed_logs_qa_without_citations_or_invented_facts()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Db.Transactions.Add(
            Txn(harness, "r-mar-in", new DateOnly(2026, 3, 5), 3200m, "Card machine settlement"));
        await harness.Db.SaveChangesAsync();

        var asked = await harness.Why.AskAsync("What should I tell my landlord?");

        Assert.False(asked.Verified);
        Assert.Equal(WhyMessages.Unverified, asked.Answer);
        Assert.Empty(asked.CitedRows);

        var log = await harness.Db.WhyAnswers.Include(a => a.Citations).SingleAsync();
        Assert.Equal(harness.OrgId, log.OrganizationId);
        Assert.Equal("What should I tell my landlord?", log.Question);
        Assert.Equal(WhyMessages.Unverified, log.Answer);
        Assert.False(log.Verified);
        Assert.Empty(log.Citations);

        var receipt = await harness.Why.GetReceiptRowsAsync(asked.AnswerId);
        Assert.Empty(receipt);
    }

    [Fact]
    public async Task Receipt_is_scoped_to_the_answer_row_ids()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Db.Transactions.AddRange(
            Txn(harness, "r-feb-in", new DateOnly(2026, 2, 14), 1000m, "Inflow"),
            Txn(harness, "r-mar-in", new DateOnly(2026, 3, 5), 700m, "Inflow"),
            Txn(harness, "other", new DateOnly(2026, 3, 6), -50m, "Should not appear"));
        await harness.Db.SaveChangesAsync();

        var asked = await harness.Why.AskAsync("Why did revenue change?");
        var receipt = await harness.Why.GetReceiptRowsAsync(asked.AnswerId);

        Assert.True(asked.Verified);
        Assert.DoesNotContain(receipt, row => row.RowId == "other");
        Assert.All(receipt, row => Assert.Contains(row.RowId, asked.CitedRowIds));
        var loaded = await harness.Why.GetAsync(asked.AnswerId);
        Assert.NotNull(loaded);
        Assert.Equal(asked.AnswerId, loaded!.AnswerId);
        Assert.Equal(receipt.Select(r => r.RowId), loaded.CitedRowIds);
    }

    private static Transaction Txn(
        Harness harness,
        string rowId,
        DateOnly date,
        decimal amount,
        string description) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = harness.OrgId,
            ImportJobId = harness.JobId,
            RowId = rowId,
            SourceRowNumber = 1,
            Date = date,
            Description = description,
            Amount = amount,
            Currency = "ZAR",
            ImportedAt = DateTime.UtcNow
        };

    private sealed class NullAzureClassifier : IAzureOpenAIIntentClassifier
    {
        public bool IsConfigured => false;

        public Task<HealthMetricKind?> TryClassifyAsync(string question, CancellationToken cancellationToken = default) =>
            Task.FromResult<HealthMetricKind?>(null);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _root;

        private Harness(string root, VhonaDbContext db, WhyService why, Guid orgId, Guid jobId)
        {
            _root = root;
            Db = db;
            Why = why;
            OrgId = orgId;
            JobId = jobId;
        }

        public VhonaDbContext Db { get; }
        public WhyService Why { get; }
        public Guid OrgId { get; }
        public Guid JobId { get; }

        public static async Task<Harness> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "vhonaai-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var orgId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            var currentUser = new StubCurrentUser(orgId, userId);
            var options = new DbContextOptionsBuilder<VhonaDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "test.db")}")
                .Options;
            var db = new VhonaDbContext(options);
            await db.Database.EnsureCreatedAsync();
            db.Organizations.Add(new Organization { Id = orgId, Name = "Harbour Street Studio", CreatedAt = DateTime.UtcNow });
            db.Users.Add(new AppUser
            {
                Id = userId,
                Email = "owner@harbourstreet.local",
                DisplayName = "Demo Owner",
                ExternalId = "demo:owner@harbourstreet.local",
                CreatedAt = DateTime.UtcNow
            });
            db.ImportJobs.Add(new ImportJob
            {
                Id = jobId,
                OrganizationId = orgId,
                CreatedByUserId = userId,
                OriginalFileName = "seed.csv",
                StoragePath = "seed.csv",
                Status = ImportStatus.Imported,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var health = new HealthKpiService(db, currentUser);
            var why = new WhyService(db, currentUser, health, new NullAzureClassifier());
            return new Harness(root, db, why, orgId, jobId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public StubCurrentUser(Guid organizationId, Guid userId)
        {
            OrganizationId = organizationId;
            UserId = userId;
        }

        public bool IsAuthenticated => true;
        public Guid UserId { get; }
        public Guid OrganizationId { get; }
        public string Email => "owner@harbourstreet.local";
        public string DisplayName => "Demo Owner";
        public string OrganizationName => "Harbour Street Studio";
    }
}
