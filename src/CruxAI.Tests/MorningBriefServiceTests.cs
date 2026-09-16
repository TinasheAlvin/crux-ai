using CruxAI.Core.Brief;
using CruxAI.Core.Entities;
using CruxAI.Core.Health;
using CruxAI.Core.Identity;
using CruxAI.Core.Time;
using CruxAI.Core.Why;
using CruxAI.Infrastructure.Brief;
using CruxAI.Infrastructure.Data;
using CruxAI.Infrastructure.Health;
using CruxAI.Infrastructure.Why;
using Microsoft.EntityFrameworkCore;

namespace CruxAI.Tests;

public class MorningBriefServiceTests
{
    [Fact]
    public async Task Opt_in_is_gated_on_a_trusted_cited_why()
    {
        await using var harness = await Harness.CreateAsync();
        var before = await harness.Briefs.OptInAsync();

        Assert.False(before.Succeeded);
        Assert.Equal(MorningBriefMessages.OptInRequired, before.Error);
        Assert.False(before.Preference.ShowOptInSheet);
        Assert.False(before.Preference.HasTrustedWhy);

        await SeedTransactionsAsync(harness);
        var why = await harness.Why.AskAsync("Why did revenue change?");
        Assert.True(why.Verified);
        Assert.NotEmpty(why.CitedRows);

        var preference = await harness.Briefs.GetPreferenceAsync();
        Assert.True(preference.HasTrustedWhy);
        Assert.True(preference.ShowOptInSheet);

        var opted = await harness.Briefs.OptInAsync();
        Assert.True(opted.Succeeded);
        Assert.True(opted.Preference.OptedIn);
        Assert.False(opted.Preference.ShowOptInSheet);

        var stored = await harness.Db.MorningBriefPreferences.SingleAsync();
        Assert.Equal(harness.OrgId, stored.OrganizationId);
        Assert.Equal(harness.UserId, stored.UserId);
        Assert.True(stored.OptedIn);
        Assert.False(stored.Dismissed);
    }

    [Fact]
    public async Task Dismiss_hides_the_sheet_once_and_blocks_later_opt_in()
    {
        await using var harness = await Harness.CreateAsync();
        await SeedTransactionsAsync(harness);
        var why = await harness.Why.AskAsync("Why did revenue change?");
        Assert.True(why.Verified);

        var shown = await harness.Briefs.GetPreferenceAsync();
        Assert.True(shown.ShowOptInSheet);

        var dismissed = await harness.Briefs.DismissAsync();
        Assert.True(dismissed.Dismissed);
        Assert.False(dismissed.ShowOptInSheet);
        Assert.False(dismissed.OptedIn);

        var again = await harness.Briefs.DismissAsync();
        Assert.True(again.Dismissed);
        Assert.False(again.ShowOptInSheet);

        var optIn = await harness.Briefs.OptInAsync();
        Assert.False(optIn.Succeeded);
        Assert.Equal(MorningBriefMessages.AlreadyDismissed, optIn.Error);
        Assert.False(optIn.Preference.OptedIn);
        Assert.False(optIn.Preference.ShowOptInSheet);

        var stored = await harness.Db.MorningBriefPreferences.SingleAsync();
        Assert.True(stored.Dismissed);
        Assert.False(stored.OptedIn);
        Assert.NotNull(stored.DismissedAt);
    }

    [Fact]
    public async Task Next_visit_returns_yesterday_snapshot_and_one_receipted_explanation()
    {
        await using var harness = await Harness.CreateAsync();
        await SeedTransactionsAsync(harness);
        await harness.Why.AskAsync("Why did revenue change?");
        var opted = await harness.Briefs.OptInAsync();
        Assert.True(opted.Succeeded);

        var landing = await harness.Briefs.GetLandingAsync();

        Assert.True(landing.OptedIn);
        Assert.True(landing.Verified);
        Assert.NotNull(landing.Snapshot);
        Assert.Equal("Mar 2026", landing.Snapshot!.CurrentPeriodLabel);
        Assert.Equal("Feb 2026", landing.Snapshot.PreviousPeriodLabel);
        Assert.Contains(landing.Snapshot.Metrics, metric => metric.Kind == HealthMetricKind.Revenue && metric.CurrentValue == 3200m);
        Assert.False(string.IsNullOrWhiteSpace(landing.Explanation));
        Assert.NotNull(landing.Receipt);
        Assert.True(landing.Receipt!.Verified);
        Assert.NotEmpty(landing.Receipt.CitedRows);
        Assert.Equal(1, new[] { "Revenue", "Expenses", "Profit" }.Count(name =>
            landing.Explanation.StartsWith(name, StringComparison.Ordinal)));

        var row = await harness.Db.MorningBriefs.Include(brief => brief.Citations).SingleAsync();
        Assert.Equal(harness.OrgId, row.OrganizationId);
        Assert.Equal(harness.UserId, row.UserId);
        Assert.Equal(DateOnly.FromDateTime(harness.Clock.UtcNow), row.BriefDate);
        Assert.True(row.Verified);
        Assert.NotEmpty(row.Citations);
        Assert.Equal(landing.Receipt.CitedRowIds.OrderBy(id => id), row.Citations.Select(c => c.RowId).OrderBy(id => id));

        var again = await harness.Briefs.GetLandingAsync();
        Assert.Equal(landing.BriefId, again.BriefId);
        Assert.Equal(1, await harness.Db.MorningBriefs.CountAsync());
    }

    [Fact]
    public async Task Next_visit_fail_closes_empty_without_a_fabricated_digest()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Db.WhyAnswers.Add(new WhyAnswer
        {
            Id = Guid.NewGuid(),
            OrganizationId = harness.OrgId,
            AskedByUserId = harness.UserId,
            Question = "Why did revenue change?",
            Answer = "cited",
            Verified = true,
            Metric = nameof(HealthMetricKind.Revenue),
            CreatedAt = DateTime.UtcNow,
            Citations =
            [
                new WhyCitation
                {
                    Id = Guid.NewGuid(),
                    RowId = "ghost",
                    Columns = "Date, Description, Amount"
                }
            ]
        });
        await harness.Db.SaveChangesAsync();

        var opted = await harness.Briefs.OptInAsync();
        Assert.True(opted.Succeeded);

        var landing = await harness.Briefs.GetLandingAsync();

        Assert.True(landing.OptedIn);
        Assert.False(landing.Verified);
        Assert.True(string.IsNullOrWhiteSpace(landing.Explanation));
        Assert.Null(landing.Receipt);
        Assert.Equal(MorningBriefMessages.Empty, MorningBriefMessages.Empty);

        var row = await harness.Db.MorningBriefs.Include(brief => brief.Citations).SingleAsync();
        Assert.False(row.Verified);
        Assert.Empty(row.Citations);
        Assert.Equal(string.Empty, row.Explanation);
        Assert.DoesNotContain("digest", row.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Not_opted_in_does_not_generate_a_brief()
    {
        await using var harness = await Harness.CreateAsync();
        await SeedTransactionsAsync(harness);
        await harness.Why.AskAsync("Why did revenue change?");

        var landing = await harness.Briefs.GetLandingAsync();
        Assert.False(landing.OptedIn);
        Assert.Null(landing.BriefId);
        Assert.Equal(0, await harness.Db.MorningBriefs.CountAsync());
    }

    [Fact]
    public async Task Functions_stub_is_the_same_on_demand_path()
    {
        await using var harness = await Harness.CreateAsync();
        await SeedTransactionsAsync(harness);
        await harness.Why.AskAsync("Why did expenses change?");
        await harness.Briefs.OptInAsync();

        var fromStub = await MorningBriefFunctionsStub.RunTimerTriggerAsync(harness.Briefs);
        Assert.True(fromStub.Verified);
        Assert.Contains("Azure Functions are not required", MorningBriefFunctionsStub.Note);
    }

    private static async Task SeedTransactionsAsync(Harness harness)
    {
        harness.Db.Transactions.AddRange(
            Txn(harness, "r-feb-in", new DateOnly(2026, 2, 14), 4200m, "Card machine settlement"),
            Txn(harness, "r-feb-retail", new DateOnly(2026, 2, 21), 650m, "Retail product sale"),
            Txn(harness, "r-feb-out", new DateOnly(2026, 2, 10), -1400m, "Colour"),
            Txn(harness, "r-mar-in", new DateOnly(2026, 3, 5), 3200m, "Card machine settlement"),
            Txn(harness, "r-mar-out", new DateOnly(2026, 3, 3), -2450m, "Wella professional stock"));
        await harness.Db.SaveChangesAsync();
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

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _root;

        private Harness(
            string root,
            CruxDbContext db,
            WhyService why,
            MorningBriefService briefs,
            FixedClock clock,
            Guid orgId,
            Guid userId,
            Guid jobId)
        {
            _root = root;
            Db = db;
            Why = why;
            Briefs = briefs;
            Clock = clock;
            OrgId = orgId;
            UserId = userId;
            JobId = jobId;
        }

        public CruxDbContext Db { get; }
        public WhyService Why { get; }
        public MorningBriefService Briefs { get; }
        public FixedClock Clock { get; }
        public Guid OrgId { get; }
        public Guid UserId { get; }
        public Guid JobId { get; }

        public static async Task<Harness> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "cruxai-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var orgId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            var currentUser = new StubCurrentUser(orgId, userId);
            var options = new DbContextOptionsBuilder<CruxDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "test.db")}")
                .Options;
            var db = new CruxDbContext(options);
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

            var clock = new FixedClock(new DateTime(2026, 3, 16, 6, 30, 0, DateTimeKind.Utc));
            var health = new HealthKpiService(db, currentUser);
            var why = new WhyService(db, currentUser, health, new NullAzureClassifier());
            var briefs = new MorningBriefService(db, currentUser, health, why, clock);
            return new Harness(root, db, why, briefs, clock, orgId, userId, jobId);
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
