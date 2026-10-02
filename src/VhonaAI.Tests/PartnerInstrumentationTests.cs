using System.Text;
using VhonaAI.Core.Analytics;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Health;
using VhonaAI.Core.Identity;
using VhonaAI.Core.Time;
using VhonaAI.Core.Why;
using VhonaAI.Infrastructure.Analytics;
using VhonaAI.Infrastructure.Brief;
using VhonaAI.Infrastructure.Csv;
using VhonaAI.Infrastructure.Data;
using VhonaAI.Infrastructure.Health;
using VhonaAI.Infrastructure.Imports;
using VhonaAI.Infrastructure.Storage;
using VhonaAI.Infrastructure.Why;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Tests;

public class PartnerInstrumentationTests
{
    [Fact]
    public async Task Persist_emits_finishes_upload()
    {
        await using var harness = await FlowHarness.CreateAsync();
        await harness.ImportSampleAsync();

        var evt = Assert.Single(harness.Log.Read());
        Assert.Equal(PartnerEventNames.FinishesUpload, evt.Name);
        Assert.Equal(harness.OrgId, evt.OrgId);
        Assert.Equal(harness.UserId, evt.UserId);
        Assert.NotEqual(default, evt.Timestamp);
        Assert.Equal("2", evt.Properties["rowCount"]);
        Assert.True(evt.Properties.ContainsKey("importJobId"));
    }

    [Fact]
    public async Task First_why_ask_in_session_emits_asks_why_session_one_once()
    {
        await using var harness = await FlowHarness.CreateAsync();
        await harness.SeedWhyTransactionsAsync();

        await harness.Why.AskAsync("Why did revenue change?");
        await harness.Why.AskAsync("Why did expenses change?");

        var names = harness.Log.Read().Select(evt => evt.Name).ToList();
        Assert.Contains(PartnerEventNames.AsksWhySessionOne, names);
        Assert.Equal(1, names.Count(name => name == PartnerEventNames.AsksWhySessionOne));
    }

    [Fact]
    public void Trust_prompt_yes_and_no_emit_named_events()
    {
        using var harness = TrackerHarness.Create();
        var answerId = Guid.NewGuid();

        harness.Analytics.TrackTrustRating(answerId, trustworthy: true);
        harness.Analytics.TrackTrustRating(answerId, trustworthy: true);
        var other = Guid.NewGuid();
        harness.Analytics.TrackTrustRating(other, trustworthy: false);

        var names = harness.Log.Read().Select(evt => evt.Name).ToList();
        Assert.Equal(
            [PartnerEventNames.RatesExplanationTrustworthy, PartnerEventNames.ReceiptDistrust],
            names);
        Assert.Equal(answerId.ToString(), harness.Log.Read()[0].Properties["answerId"]);
    }

    [Fact]
    public void Receipt_open_emits_once_per_answer()
    {
        using var harness = TrackerHarness.Create();
        var answerId = Guid.NewGuid();
        harness.Analytics.TrackReceiptOpen(answerId);
        harness.Analytics.TrackReceiptOpen(answerId);

        var evt = Assert.Single(harness.Log.Read());
        Assert.Equal(PartnerEventNames.ReceiptOpen, evt.Name);
        Assert.Equal(answerId.ToString(), evt.Properties["answerId"]);
    }

    [Fact]
    public void Waitlist_emits_pay_or_waitlist_signal()
    {
        using var harness = TrackerHarness.Create();
        harness.Analytics.TrackWaitlist("home");
        harness.Analytics.TrackWaitlist("brief");

        var evt = Assert.Single(harness.Log.Read());
        Assert.Equal(PartnerEventNames.PayOrWaitlistSignal, evt.Name);
        Assert.Equal("home", evt.Properties["surface"]);
    }

    [Fact]
    public void Map_abandon_guard_skips_prerender_and_saved_mapping()
    {
        var guard = new MapAbandonGuard();
        Assert.False(guard.ShouldTrackAbandon);

        guard.MarkInteractive();
        Assert.True(guard.ShouldTrackAbandon);

        guard.MarkSaved();
        Assert.False(guard.ShouldTrackAbandon);

        using var harness = TrackerHarness.Create();
        harness.Analytics.TrackMapAbandon(Guid.NewGuid());
        Assert.Equal(PartnerEventNames.MapAbandon, Assert.Single(harness.Log.Read()).Name);
    }

    [Fact]
    public async Task Brief_shown_on_next_visit_within_7_days_emits_return_event()
    {
        await using var harness = await FlowHarness.CreateAsync();
        await harness.SeedWhyTransactionsAsync();
        await harness.Why.AskAsync("Why did revenue change?");
        var opted = await harness.Briefs.OptInAsync();
        Assert.True(opted.Succeeded);

        var sameVisit = await harness.Briefs.GetLandingAsync();
        Assert.True(sameVisit.OptedIn);
        Assert.DoesNotContain(harness.Log.Read(), evt => evt.Name == PartnerEventNames.ReturnsForBriefWithin7Days);

        harness.Clock.UtcNow = harness.Clock.UtcNow.AddDays(2);
        var nextVisit = harness.NewVisit();
        var landing = await nextVisit.Briefs.GetLandingAsync();
        Assert.True(landing.OptedIn);

        var returned = Assert.Single(nextVisit.Log.Read(), evt => evt.Name == PartnerEventNames.ReturnsForBriefWithin7Days);
        Assert.Equal(harness.OrgId, returned.OrgId);
        Assert.Equal(harness.UserId, returned.UserId);
    }

    [Fact]
    public async Task Brief_shown_after_7_days_does_not_emit_return_event()
    {
        await using var harness = await FlowHarness.CreateAsync();
        await harness.SeedWhyTransactionsAsync();
        await harness.Why.AskAsync("Why did revenue change?");
        await harness.Briefs.OptInAsync();

        harness.Clock.UtcNow = harness.Clock.UtcNow.AddDays(7).AddSeconds(1);
        var nextVisit = harness.NewVisit();
        await nextVisit.Briefs.GetLandingAsync();

        Assert.DoesNotContain(nextVisit.Log.Read(), evt => evt.Name == PartnerEventNames.ReturnsForBriefWithin7Days);
    }

    [Fact]
    public void Jsonl_file_sink_round_trips_events()
    {
        var root = Path.Combine(Path.GetTempPath(), "vhonaai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "partner-events.jsonl");
        var log = new JsonlFileEventLog(path);
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        log.Append(new AnalyticsEvent
        {
            Name = PartnerEventNames.FinishesUpload,
            OrgId = orgId,
            UserId = userId,
            Timestamp = new DateTime(2026, 9, 16, 21, 0, 0, DateTimeKind.Utc),
            Properties = new Dictionary<string, string> { ["rowCount"] = "3" }
        });

        var loaded = new JsonlFileEventLog(path).Read();
        var evt = Assert.Single(loaded);
        Assert.Equal(PartnerEventNames.FinishesUpload, evt.Name);
        Assert.Equal(orgId, evt.OrgId);
        Assert.Equal(userId, evt.UserId);
        Assert.Equal("3", evt.Properties["rowCount"]);
        Assert.Contains("\"name\":\"finishes_upload\"", File.ReadAllText(path));
    }

    [Fact]
    public void Sqlite_sink_appends_and_reads_events()
    {
        var root = Path.Combine(Path.GetTempPath(), "vhonaai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "partner-events.db");
        using var log = new SqliteEventLog(path);
        log.Append(new AnalyticsEvent
        {
            Name = PartnerEventNames.ReceiptOpen,
            OrgId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Properties = new Dictionary<string, string> { ["answerId"] = Guid.NewGuid().ToString() }
        });

        Assert.Equal(PartnerEventNames.ReceiptOpen, Assert.Single(log.Read()).Name);
    }

    [Fact]
    public void Partner_event_names_cover_the_scoreboard()
    {
        Assert.Contains(PartnerEventNames.FinishesUpload, PartnerEventNames.All);
        Assert.Contains(PartnerEventNames.AsksWhySessionOne, PartnerEventNames.All);
        Assert.Contains(PartnerEventNames.RatesExplanationTrustworthy, PartnerEventNames.All);
        Assert.Contains(PartnerEventNames.ReturnsForBriefWithin7Days, PartnerEventNames.All);
        Assert.Contains(PartnerEventNames.PayOrWaitlistSignal, PartnerEventNames.All);
        Assert.Contains(PartnerEventNames.MapAbandon, PartnerEventNames.All);
        Assert.Contains(PartnerEventNames.ReceiptOpen, PartnerEventNames.All);
        Assert.Contains(PartnerEventNames.ReceiptDistrust, PartnerEventNames.All);
    }

    private sealed class NullAzureClassifier : IAzureOpenAIIntentClassifier
    {
        public bool IsConfigured => false;

        public Task<HealthMetricKind?> TryClassifyAsync(string question, CancellationToken cancellationToken = default) =>
            Task.FromResult<HealthMetricKind?>(null);
    }

    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; set; }
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

    private sealed class TrackerHarness : IDisposable
    {
        private TrackerHarness(InMemoryEventLog log, AnalyticsService analytics)
        {
            Log = log;
            Analytics = analytics;
        }

        public InMemoryEventLog Log { get; }
        public AnalyticsService Analytics { get; }

        public static TrackerHarness Create()
        {
            var orgId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var log = new InMemoryEventLog();
            var analytics = new AnalyticsService(
                log,
                new StubCurrentUser(orgId, userId),
                new MutableClock(new DateTime(2026, 3, 16, 6, 30, 0, DateTimeKind.Utc)),
                new PartnerSession());
            return new TrackerHarness(log, analytics);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FlowHarness : IAsyncDisposable
    {
        private readonly string _root;
        private readonly StubCurrentUser _currentUser;
        private readonly HealthKpiService _health;

        private FlowHarness(
            string root,
            VhonaDbContext db,
            StubCurrentUser currentUser,
            HealthKpiService health,
            WhyService why,
            MorningBriefService briefs,
            ImportService imports,
            InMemoryEventLog log,
            MutableClock clock,
            Guid orgId,
            Guid userId,
            Guid jobId)
        {
            _root = root;
            _currentUser = currentUser;
            _health = health;
            Db = db;
            Why = why;
            Briefs = briefs;
            Imports = imports;
            Log = log;
            Clock = clock;
            OrgId = orgId;
            UserId = userId;
            JobId = jobId;
        }

        public VhonaDbContext Db { get; }
        public WhyService Why { get; }
        public MorningBriefService Briefs { get; }
        public ImportService Imports { get; }
        public InMemoryEventLog Log { get; }
        public MutableClock Clock { get; }
        public Guid OrgId { get; }
        public Guid UserId { get; }
        public Guid JobId { get; }

        public static async Task<FlowHarness> CreateAsync()
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

            var clock = new MutableClock(new DateTime(2026, 3, 16, 6, 30, 0, DateTimeKind.Utc));
            var log = new InMemoryEventLog();
            var analytics = new AnalyticsService(log, currentUser, clock, new PartnerSession());
            var health = new HealthKpiService(db, currentUser);
            var why = new WhyService(db, currentUser, health, new NullAzureClassifier(), analytics);
            var briefs = new MorningBriefService(db, currentUser, health, why, clock, analytics);
            var imports = new ImportService(
                db,
                new LocalFileStorage(Path.Combine(root, "uploads")),
                new CsvHelperReader(),
                currentUser,
                analytics);
            return new FlowHarness(root, db, currentUser, health, why, briefs, imports, log, clock, orgId, userId, jobId);
        }

        public FlowHarness NewVisit()
        {
            var log = new InMemoryEventLog();
            var analytics = new AnalyticsService(log, _currentUser, Clock, new PartnerSession());
            var why = new WhyService(Db, _currentUser, _health, new NullAzureClassifier(), analytics);
            var briefs = new MorningBriefService(Db, _currentUser, _health, why, Clock, analytics);
            return new FlowHarness(_root, Db, _currentUser, _health, why, briefs, Imports, log, Clock, OrgId, UserId, JobId);
        }

        public async Task ImportSampleAsync()
        {
            const string csv = """
                Txn Date,Details,ZAR Amount
                2026-03-02,Cut and blow dry,-450.00
                2026-03-14,Card machine settlement,3200.00
                """;
            await using var upload = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var job = await Imports.CreateFromUploadAsync(upload, "sample.csv", upload.Length);
            await Imports.SaveMappingAsync(job, Imports.GetMapping(job, await Imports.LoadTableAsync(job)));
            await Imports.PersistAsync(job, includeOnlyValid: false);
        }

        public async Task SeedWhyTransactionsAsync()
        {
            Db.Transactions.AddRange(
                Txn("r-feb-in", new DateOnly(2026, 2, 14), 4200m, "Card machine settlement"),
                Txn("r-feb-retail", new DateOnly(2026, 2, 21), 650m, "Retail product sale"),
                Txn("r-feb-out", new DateOnly(2026, 2, 10), -1400m, "Colour"),
                Txn("r-mar-in", new DateOnly(2026, 3, 5), 3200m, "Card machine settlement"),
                Txn("r-mar-out", new DateOnly(2026, 3, 3), -2450m, "Wella professional stock"));
            await Db.SaveChangesAsync();
        }

        private Transaction Txn(string rowId, DateOnly date, decimal amount, string description) =>
            new()
            {
                Id = Guid.NewGuid(),
                OrganizationId = OrgId,
                ImportJobId = JobId,
                RowId = rowId,
                SourceRowNumber = 1,
                Date = date,
                Description = description,
                Amount = amount,
                Currency = "ZAR",
                ImportedAt = DateTime.UtcNow
            };

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
}
