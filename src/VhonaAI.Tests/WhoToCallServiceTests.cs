using System.Reflection;
using System.Text;
using VhonaAI.Application.Calling;
using VhonaAI.Core.Calling;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Health;
using VhonaAI.Core.Identity;
using VhonaAI.Core.Time;
using VhonaAI.Core.Why;
using VhonaAI.Infrastructure.Brief;
using VhonaAI.Infrastructure.Calling;
using VhonaAI.Infrastructure.Csv;
using VhonaAI.Infrastructure.Data;
using VhonaAI.Infrastructure.Imports;
using VhonaAI.Infrastructure.Storage;
using VhonaAI.Infrastructure.Health;
using VhonaAI.Infrastructure.Why;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Tests;

public class WhoToCallServiceTests
{
    private static readonly DateTime Now = new(2026, 4, 2, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Sample_load_reproduces_the_four_flags_and_drafts_only_the_late_one()
    {
        var (db, user, clock) = await OpenAsync();
        await using (db)
        {
            var service = Service(db, user, clock);
            var empty = await service.GetListAsync();
            Assert.False(empty.HasInvoices);
            Assert.Empty(empty.Flags);

            await service.LoadSampleAsync();
            var again = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoadSampleAsync());
            Assert.Contains("no invoices", again.Message, StringComparison.OrdinalIgnoreCase);

            var list = await service.GetListAsync();
            Assert.Equal(new DateOnly(2026, 3, 31), list.AsAt);
            Assert.True(list.HasInvoices);
            Assert.True(list.UsingSample);
            Assert.False(list.CanLoadSample);
            Assert.Equal(
                ["Sondela Dental Studio", "Marula Ridge Office Park", "Tamboti Clinic Rooms", "Kopano Guest Lodge"],
                list.Flags.Select(flag => flag.CustomerName).ToArray());
            Assert.All(list.Flags, flag => Assert.NotEmpty(flag.CitedRowIds));
            Assert.Equal(4, await db.Customers.CountAsync());
            Assert.True(await db.Payments.AnyAsync());

            var late = list.Flags[0];
            var detail = await service.GetFlagAsync(late.CustomerId);
            Assert.NotNull(detail);
            Assert.Equal(ReminderDraftTemplate.NotSent, detail.DraftStatus);
            Assert.Contains("INV 2295", detail.DraftBody);
            Assert.Contains("R21 350", detail.DraftBody);
            Assert.Contains("Harbour Street Studio", detail.DraftBody);
            Assert.StartsWith("https://wa.me/27820001111?text=", detail.WhatsAppLink);
            Assert.DoesNotContain("api.whatsapp.com", detail.WhatsAppLink);
            Assert.StartsWith("mailto:naledi@sondela.example?", detail.EmailLink);
            Assert.Equal(WhoToCallResult.ReceiptFooter, detail.ReceiptFooter);

            var stopped = await service.GetFlagAsync(list.Flags[1].CustomerId);
            Assert.Null(stopped!.DraftBody);
            Assert.Null(stopped.WhatsAppLink);
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SaveDraftAsync(list.Flags[1].CustomerId, "Hello"));
            Assert.Contains("late", refused.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Edited_draft_is_kept_and_stays_not_sent()
    {
        var (db, user, clock) = await OpenAsync();
        await using (db)
        {
            var service = Service(db, user, clock);
            await service.LoadSampleAsync();
            var late = (await service.GetListAsync()).Flags[0];
            var saved = await service.SaveDraftAsync(late.CustomerId, "Please see INV 2295 when you can.");
            Assert.Equal(ReminderDraftTemplate.NotSent, saved.Status);
            Assert.Equal(ReminderDraftTemplate.NotSent, (await db.ReminderDrafts.SingleAsync()).Status);

            var detail = await service.GetFlagAsync(late.CustomerId);
            Assert.Equal("Please see INV 2295 when you can.", detail!.DraftBody);
            Assert.Equal(ReminderDraftTemplate.NotSent, detail.DraftStatus);
        }
    }

    [Fact]
    public async Task Actions_hide_and_later_invoices_bring_a_flag_back()
    {
        var (db, user, clock) = await OpenAsync();
        await using (db)
        {
            var service = Service(db, user, clock);
            await service.LoadSampleAsync();
            var list = await service.GetListAsync();
            var sondela = list.Flags.Single(flag => flag.CustomerName == "Sondela Dental Studio");
            var marula = list.Flags.Single(flag => flag.CustomerName == "Marula Ridge Office Park");

            await service.RecordActionAsync(marula.CustomerId, CallActionKind.Called, null, "Spoke to the office");
            Assert.Contains((await service.GetListAsync()).Flags, flag => flag.CustomerId == marula.CustomerId);
            var history = await service.GetHistoryAsync(marula.CustomerId);
            Assert.Equal(CallActionKind.Called, history[0].Kind);
            Assert.Equal("Ada", history[0].ActorName);

            var past = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RecordActionAsync(sondela.CustomerId, CallActionKind.Snoozed, new DateOnly(2026, 4, 1), null));
            Assert.Contains("today or a later date", past.Message, StringComparison.OrdinalIgnoreCase);
            await service.RecordActionAsync(sondela.CustomerId, CallActionKind.Snoozed, new DateOnly(2026, 4, 2), null);
            Assert.DoesNotContain((await service.GetListAsync()).Flags, flag => flag.CustomerId == sondela.CustomerId);
            Assert.Null(await service.GetFlagAsync(sondela.CustomerId));
            Assert.NotEmpty(await service.GetHistoryAsync(sondela.CustomerId));

            var tamboti = list.Flags.Single(flag => flag.CustomerName == "Tamboti Clinic Rooms");
            await service.RecordActionAsync(tamboti.CustomerId, CallActionKind.NotAConcern, null, null);
            Assert.DoesNotContain((await service.GetListAsync()).Flags, flag => flag.CustomerId == tamboti.CustomerId);

            var tambotiCustomer = await db.Customers.SingleAsync(item => item.Id == tamboti.CustomerId);
            db.Invoices.Add(new Invoice
            {
                Id = Guid.NewGuid(),
                OrganizationId = user.OrganizationId,
                CustomerId = tambotiCustomer.Id,
                RowId = "tamboti-new",
                Number = "TC-NEW",
                InvoiceDate = new DateOnly(2026, 2, 20),
                Amount = 2750m,
                AmountDue = 0,
                Status = InvoiceStatus.Paid,
                PaidDate = new DateOnly(2026, 2, 20),
                Currency = "ZAR",
                ImportedAt = Now.AddDays(1)
            });
            await db.SaveChangesAsync();
            Assert.Contains((await service.GetListAsync()).Flags, flag => flag.CustomerId == tamboti.CustomerId);

            clock.UtcNow = clock.UtcNow.AddMinutes(5);
            await service.RecordActionAsync(sondela.CustomerId, CallActionKind.Paid, null, null);
            Assert.DoesNotContain((await service.GetListAsync()).Flags, flag => flag.CustomerId == sondela.CustomerId);
            db.Invoices.Add(new Invoice
            {
                Id = Guid.NewGuid(),
                OrganizationId = user.OrganizationId,
                CustomerId = sondela.CustomerId,
                RowId = "sondela-extra",
                Number = "INV 2400",
                InvoiceDate = new DateOnly(2026, 3, 20),
                DueDate = new DateOnly(2026, 3, 25),
                Amount = 100m,
                AmountDue = 100m,
                Status = InvoiceStatus.Open,
                Currency = "ZAR",
                ImportedAt = Now
            });
            await db.SaveChangesAsync();
            Assert.Contains((await service.GetListAsync()).Flags, flag => flag.CustomerId == sondela.CustomerId);
        }
    }

    [Fact]
    public async Task An_imported_overdue_invoice_is_flagged_late_with_its_row()
    {
        var (db, user, clock) = await OpenAsync();
        await using (db)
        {
            var root = Path.Combine(Path.GetTempPath(), "vhonaai-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var imports = new ImportService(db, new LocalFileStorage(Path.Combine(root, "uploads")), new CsvHelperReader(), user);
            const string csv = """
                Customer,Invoice Number,Invoice Date,Due Date,Amount,Amount Due,Status
                Naledi Khumalo,INV-2295,2026-03-31,2026-02-12,12400.00,12400.00,Overdue
                """;
            await using var upload = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var job = await imports.CreateFromUploadAsync(upload, "overdue.csv", upload.Length, ImportKind.Invoices);
            await imports.SaveMappingAsync(job, imports.GetMapping(job, await imports.LoadTableAsync(job)));
            var imported = await imports.PersistInvoicesAsync(job, includeOnlyValid: false);

            var stored = await db.Invoices.SingleAsync();
            Assert.Equal(InvoiceStatus.Overdue, stored.Status);
            var rowId = imported.Invoices[0].RowId;
            Assert.Equal(rowId, stored.RowId);

            var flag = Assert.Single((await Service(db, user, clock).GetListAsync()).Flags);
            Assert.Equal(CallFlagKind.Late, flag.Kind);
            Assert.Equal("Naledi Khumalo", flag.CustomerName);
            Assert.Equal(rowId, Assert.Single(flag.CitedRowIds));
            Assert.Equal(47, flag.OldestDaysOverdue);
            Assert.Equal(12400m, flag.OpenTotal);
            Assert.Contains("INV-2295", flag.DraftBody);

            var detail = await Service(db, user, clock).GetFlagAsync(flag.CustomerId);
            var open = Assert.Single(detail!.Flag.Late!.OpenInvoices);
            Assert.Equal(rowId, open.RowId);
            Assert.Equal("INV-2295", open.Number);
            Assert.Equal(47, open.DaysOverdue);
            Assert.True(open.IsOpen);
        }
    }

    [Fact]
    public async Task Paying_the_open_invoices_clears_the_late_flag()
    {
        var (db, user, clock) = await OpenAsync();
        await using (db)
        {
            var service = Service(db, user, clock);
            await service.LoadSampleAsync();
            var open = await db.Invoices.Where(item => item.Status == InvoiceStatus.Open).ToListAsync();
            Assert.Equal(2, open.Count);
            foreach (var invoice in open)
            {
                invoice.Status = InvoiceStatus.Paid;
                invoice.AmountDue = 0;
                invoice.PaidDate = new DateOnly(2026, 3, 30);
            }

            await db.SaveChangesAsync();
            var list = await service.GetListAsync();
            Assert.DoesNotContain(list.Flags, flag => flag.CustomerName == "Sondela Dental Studio");
            Assert.Contains(list.Flags, flag => flag.Kind == CallFlagKind.Stopped);
            Assert.Contains(list.Flags, flag => flag.Kind == CallFlagKind.Dropped);
        }
    }

    [Fact]
    public async Task Threshold_changes_rerun_the_flags()
    {
        var (db, user, clock) = await OpenAsync();
        await using (db)
        {
            var service = Service(db, user, clock);
            await service.LoadSampleAsync();
            Assert.Equal(4, (await service.GetListAsync()).Flags.Count);

            var settings = new CallSettingsService(db, user);
            await settings.UpdateAsync(new CallThresholdSettingsUpdate(3, 50m, 2, 3));
            Assert.Equal(
                ["Sondela Dental Studio", "Kopano Guest Lodge"],
                (await service.GetListAsync()).Flags.Select(flag => flag.CustomerName).ToArray());
        }
    }

    [Fact]
    public async Task A_member_can_read_and_cannot_change_who_to_call()
    {
        var (db, owner, clock) = await OpenAsync();
        await using (db)
        {
            await Service(db, owner, clock).LoadSampleAsync();
            var member = new Person(owner.OrganizationId, Guid.NewGuid(), "Member", owner.OrganizationName);
            var service = Service(db, member, clock);
            var list = await service.GetListAsync();
            Assert.False(list.IsOwner);
            Assert.False(list.CanLoadSample);
            Assert.NotEmpty(list.Flags);

            var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoadSampleAsync());
            Assert.Contains("owner", blocked.Message, StringComparison.OrdinalIgnoreCase);
            var draft = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SaveDraftAsync(list.Flags[0].CustomerId, "Hello there"));
            Assert.Contains("owner", draft.Message, StringComparison.OrdinalIgnoreCase);
            var action = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RecordActionAsync(list.Flags[0].CustomerId, CallActionKind.Called, null, null));
            Assert.Contains("owner", action.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Who_to_call_stays_inside_the_signed_in_business()
    {
        var studioId = Guid.NewGuid();
        var bakeryId = Guid.NewGuid();
        var studioUserId = Guid.NewGuid();
        var options = Sqlite();
        await using (var setup = new VhonaDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var now = Now;
            setup.Organizations.AddRange(
                new Organization { Id = studioId, Name = "Harbour Street Studio", CreatedAt = now },
                new Organization { Id = bakeryId, Name = "Market Bakery", CreatedAt = now });
            setup.Users.Add(new AppUser
            {
                Id = studioUserId,
                ExternalId = "studio",
                Email = "owner@example.com",
                DisplayName = "Ada",
                CreatedAt = now
            });
            await setup.SaveChangesAsync();
        }

        var studio = new Person(studioId, studioUserId, "Owner", "Harbour Street Studio");
        var bakeryUser = new Person(bakeryId, Guid.NewGuid(), "Owner", "Market Bakery");
        await using var studioDb = new VhonaDbContext(options, studio);
        await using var bakeryDb = new VhonaDbContext(options, bakeryUser);
        var clock = new FixedClock(Now);
        await Service(studioDb, studio, clock).LoadSampleAsync();

        var bakeryCustomer = Guid.NewGuid();
        bakeryDb.Customers.Add(new Customer
        {
            Id = bakeryCustomer,
            OrganizationId = bakeryId,
            RowId = "bakery-customer",
            Name = "Bakery Late",
            NormalizedName = "BAKERY LATE",
            CreatedAt = Now,
            UpdatedAt = Now
        });
        bakeryDb.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(),
            OrganizationId = bakeryId,
            CustomerId = bakeryCustomer,
            RowId = "bakery-inv",
            Number = "B-1",
            InvoiceDate = new DateOnly(2026, 3, 31),
            DueDate = new DateOnly(2026, 2, 1),
            Amount = 500m,
            AmountDue = 500m,
            Status = InvoiceStatus.Open,
            Currency = "ZAR",
            ImportedAt = Now
        });
        await bakeryDb.SaveChangesAsync();

        var studioList = await Service(studioDb, studio, clock).GetListAsync();
        Assert.DoesNotContain(studioList.Flags, flag => flag.CustomerName == "Bakery Late");
        Assert.Equal(4, studioList.Flags.Count);
        Assert.Null(await Service(studioDb, studio, clock).GetFlagAsync(bakeryCustomer));
        Assert.Empty(await Service(studioDb, studio, clock).GetHistoryAsync(bakeryCustomer));

        var bakeryList = await Service(bakeryDb, bakeryUser, clock).GetListAsync();
        Assert.Equal(["Bakery Late"], bakeryList.Flags.Select(flag => flag.CustomerName).ToArray());
        Assert.DoesNotContain(bakeryList.Flags, flag => flag.CustomerName == "Sondela Dental Studio");

        studioDb.CallCustomerActions.Add(new CallCustomerAction
        {
            Id = Guid.NewGuid(),
            OrganizationId = bakeryId,
            CustomerId = bakeryCustomer,
            Kind = CallActionKind.Called,
            ActedByUserId = studio.UserId,
            At = Now
        });
        var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() => studioDb.SaveChangesAsync());
        Assert.Contains("different business", blocked.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Morning_brief_includes_a_who_to_call_line_only_when_opted_in()
    {
        var (db, user, clock) = await OpenAsync();
        await using (db)
        {
            var calls = Service(db, user, clock);
            await calls.LoadSampleAsync();
            var briefs = Briefs(db, user, clock, calls);

            var hidden = await briefs.GetLandingAsync();
            Assert.False(hidden.OptedIn);
            Assert.Null(hidden.WhoToCall);

            db.MorningBriefPreferences.Add(new MorningBriefPreference
            {
                Id = Guid.NewGuid(),
                OrganizationId = user.OrganizationId,
                UserId = user.UserId,
                OptedIn = true,
                OptedInAt = Now
            });
            await db.SaveChangesAsync();

            var landing = await briefs.GetLandingAsync();
            Assert.True(landing.OptedIn);
            Assert.NotNull(landing.WhoToCall);
            Assert.Equal(4, landing.WhoToCall.CustomerCount);
            Assert.Equal("4 customers to call", landing.WhoToCall.Text);
        }
    }

    [Fact]
    public void There_is_no_sending_code_path()
    {
        var names = typeof(IWhoToCallAppService).GetMethods().Select(method => method.Name).ToList();
        Assert.DoesNotContain(names, name => name.Contains("Send", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(WhoToCallService).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Select(method => method.Name),
            name => name.Contains("Send", StringComparison.OrdinalIgnoreCase));

        var root = RepoRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src", "VhonaAI.Core", "Calling"), "*.*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "src", "VhonaAI.Infrastructure", "Calling"), "*.*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "src", "VhonaAI.Web", "Components"), "WhoToCall*.razor", SearchOption.AllDirectories));
        var forbidden = new[]
        {
            "SmtpClient",
            "MailKit",
            "SendGrid",
            "Twilio",
            "api.whatsapp.com",
            "graph.facebook.com",
            "HttpClient",
            "SendAsync"
        };
        var hits = files
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => forbidden.Where(token => file.Text.Contains(token, StringComparison.Ordinal)).Select(token => $"{token} in {file.Path}"))
            .ToList();
        Assert.Empty(hits);
        Assert.Contains("wa.me", File.ReadAllText(Path.Combine(root, "src", "VhonaAI.Core", "Calling", "ReminderHandoff.cs")));
        Assert.Contains("mailto:", File.ReadAllText(Path.Combine(root, "src", "VhonaAI.Core", "Calling", "ReminderHandoff.cs")));
    }

    private static WhoToCallService Service(VhonaDbContext db, Person user, FixedClock clock) =>
        new(db, user, clock, new CallSettingsService(db, user));

    private static MorningBriefService Briefs(VhonaDbContext db, Person user, FixedClock clock, WhoToCallService calls)
    {
        var health = new HealthKpiService(db, user);
        var why = new WhyService(db, user, health, new NullAzure());
        return new MorningBriefService(db, user, health, why, clock, calls: calls);
    }

    private static async Task<(VhonaDbContext Db, Person User, FixedClock Clock)> OpenAsync()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var user = new Person(organizationId, userId, "Owner", "Harbour Street Studio");
        var options = Sqlite();
        var db = new VhonaDbContext(options, user);
        await db.Database.EnsureCreatedAsync();
        db.Organizations.Add(new Organization { Id = organizationId, Name = user.OrganizationName, CreatedAt = Now });
        db.Users.Add(new AppUser
        {
            Id = userId,
            ExternalId = "ada",
            Email = user.Email,
            DisplayName = user.DisplayName,
            CreatedAt = Now
        });
        await db.SaveChangesAsync();
        return (db, user, new FixedClock(Now));
    }

    private static DbContextOptions<VhonaDbContext> Sqlite()
    {
        var root = Path.Combine(Path.GetTempPath(), "vhonaai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new DbContextOptionsBuilder<VhonaDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, "test.db")}")
            .Options;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "VhonaAI.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }

    private sealed class Person : ICurrentUser
    {
        public Person(Guid organizationId, Guid userId, string role, string organizationName)
        {
            OrganizationId = organizationId;
            UserId = userId;
            Role = role;
            OrganizationName = organizationName;
        }

        public bool IsAuthenticated => true;
        public Guid UserId { get; }
        public Guid OrganizationId { get; }
        public string Email => "owner@example.com";
        public string DisplayName => "Ada";
        public string OrganizationName { get; }
        public string Role { get; }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;
        public DateTime UtcNow { get; set; }
    }

    private sealed class NullAzure : IAzureOpenAIIntentClassifier
    {
        public bool IsConfigured => false;
        public Task<HealthMetricKind?> TryClassifyAsync(string question, CancellationToken cancellationToken = default) =>
            Task.FromResult<HealthMetricKind?>(null);
    }
}
