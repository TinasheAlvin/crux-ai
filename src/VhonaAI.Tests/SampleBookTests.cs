using System.Text.Json;
using VhonaAI.Core.Calling;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Health;
using VhonaAI.Core.Identity;
using VhonaAI.Core.Time;
using VhonaAI.Infrastructure.Calling;
using VhonaAI.Infrastructure.Data;
using VhonaAI.Infrastructure.Health;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Tests;

public class SampleBookTests
{
    private static readonly DateTime Now = new(2026, 7, 20, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Karoo_books_match_the_published_months_and_the_three_flags()
    {
        var ledger = KarooKitchenBook.Build();
        Assert.InRange(ledger.Customers.Count, 12, 15);
        Assert.Equal(KarooKitchenBook.AsAt, LatestCountable(ledger));
        Assert.DoesNotContain(ledger.Transactions, row => row.Date >= new DateOnly(2026, 7, 1));

        foreach (var (year, month, revenue) in KarooKitchenBook.RevenueMonths)
        {
            var takings = ledger.Transactions
                .Where(row => row.Date.Year == year && row.Date.Month == month && row.Amount > 0)
                .Sum(row => row.Amount);
            Assert.Equal(revenue, takings);
        }

        var june = ledger.Transactions.Where(row => row.Date.Year == 2026 && row.Date.Month == 6).ToList();
        Assert.Equal(KarooKitchenBook.June2026Expenses, june.Where(row => row.Amount < 0).Sum(row => -row.Amount));
        Assert.Equal(KarooKitchenBook.June2026Food, -june.Single(row => row.Category == "Food and beverage").Amount);
        Assert.Equal(KarooKitchenBook.GardensJune, june.Where(row => row.Counterparty == "Gardens").Sum(row => row.Amount));
        Assert.Equal(KarooKitchenBook.StellenboschJune, june.Where(row => row.Counterparty == "Stellenbosch").Sum(row => row.Amount));
        Assert.Equal(KarooKitchenBook.SeaPointJune, june.Where(row => row.Counterparty == "Sea Point").Sum(row => row.Amount));
        Assert.Equal(KarooKitchenBook.June2026Cash, june.OrderBy(row => row.Date).ThenBy(row => row.SourceRowNumber).Last().Balance);
        Assert.All(june, row => Assert.NotEqual(0, row.SourceRowNumber));

        var june2025 = ledger.Transactions.Where(row => row.Date.Year == 2025 && row.Date.Month == 6).ToList();
        Assert.Equal(KarooKitchenBook.June2025Revenue, june2025.Where(row => row.Amount > 0).Sum(row => row.Amount));
        Assert.Equal(KarooKitchenBook.June2025Expenses, june2025.Where(row => row.Amount < 0).Sum(row => -row.Amount));

        var mayExpenses = ledger.Transactions.Where(row => row.Date.Year == 2026 && row.Date.Month == 5 && row.Amount < 0).Sum(row => -row.Amount);
        var revenueMove = Math.Abs(KarooKitchenBook.June2026Revenue - KarooKitchenBook.May2026Revenue);
        var expenseMove = Math.Abs(KarooKitchenBook.June2026Expenses - mayExpenses);
        Assert.True(revenueMove > expenseMove);

        var flags = Evaluate(ledger);
        Assert.Equal(
            [KarooKitchenBook.LateCustomer, KarooKitchenBook.StoppedCustomer, KarooKitchenBook.DroppedCustomer],
            flags.Select(flag => flag.CustomerName).ToArray());
        Assert.Equal([CallFlagKind.Late, CallFlagKind.Stopped, CallFlagKind.Dropped], flags.Select(flag => flag.Kind).ToArray());
        Assert.Contains(KarooKitchenBook.LateInvoiceRowId, flags[0].CitedRowIds);
        AssertCitedRows(ledger, flags);
        Assert.Contains(ledger.Customers.SelectMany(customer => customer.Invoices), invoice => invoice.Status == InvoiceStatus.Void);
        Assert.NotEmpty(ledger.CreditNotes);
        Assert.DoesNotContain(ledger.Transactions, row => row.Date.Month == 7 && row.Date.Year == 2026);
    }

    [Fact]
    public void Axum_books_keep_the_demo_order_history_and_the_three_flags()
    {
        var ledger = AxumHomeBook.Build();
        Assert.Equal(AxumHomeBook.AsAt, LatestCountable(ledger));
        Assert.Equal(25, AxumDemoFigures.Skus.Length);
        Assert.Equal(37, AxumDemoFigures.Regions.Length);
        Assert.Equal(715, AxumDemoFigures.ModelCount);
        Assert.Equal(308092, AxumHomeBook.NextTwoDayUnits);

        Assert.Equal(25, AxumHomeBook.WholesalePrices.Count);
        Assert.All(AxumDemoFigures.Skus, sku =>
        {
            var price = AxumHomeBook.Price(sku);
            Assert.InRange(price, 18m, 180m);
        });
        var historyRows = ledger.Transactions.Where(row => row.RowId.StartsWith("axum-h-", StringComparison.Ordinal)).ToList();
        Assert.Equal(AxumDemoFigures.History.Length, historyRows.Count);
        Assert.Equal(
            AxumDemoFigures.History.Sum(row => row.Units * AxumHomeBook.Price(row.Sku)),
            historyRows.Sum(row => row.Amount));
        Assert.Contains(historyRows, row => row.Date == AxumHomeBook.AsAt && row.Description.StartsWith("DW190 Dishwashing liquid", StringComparison.Ordinal));
        var march = ledger.Transactions.Where(row => row.Date.Year == 2026 && row.Date.Month == 3).ToList();
        var marchRevenue = march.Where(row => row.Amount > 0).Sum(row => row.Amount);
        var marchStock = -march.Single(row => row.Category == "Stock purchases").Amount;
        Assert.Equal(decimal.Round(marchRevenue * 0.61m, 0), marchStock);
        Assert.Contains(march, row => row.Category == "Delivery");
        Assert.Contains(march, row => row.Category == "Wages");
        Assert.Contains(march, row => row.Category == "Rent");
        var februaryClose = ledger.Transactions
            .Where(row => row.Date.Year == 2026 && row.Date.Month == 2)
            .OrderBy(row => row.Date)
            .ThenBy(row => row.SourceRowNumber)
            .Last()
            .Balance;
        Assert.Equal(AxumHomeBook.FebruaryCash, februaryClose);

        var months = ledger.Transactions.Select(row => new DateOnly(row.Date.Year, row.Date.Month, 1)).Distinct().OrderBy(month => month).ToList();
        Assert.Equal(new DateOnly(2025, 5, 1), months[0]);
        Assert.Equal(new DateOnly(2026, 4, 1), months[^1]);
        Assert.Equal(12, months.Count);
        Assert.DoesNotContain(ledger.Transactions, row => row.Date >= new DateOnly(2026, 5, 1));

        var april = ledger.Transactions.Where(row => row.Date.Year == 2026 && row.Date.Month == 4).ToList();
        Assert.All(april, row => Assert.True(row.SourceRowNumber > 0));
        var cash = april.OrderBy(row => row.Date).ThenBy(row => row.SourceRowNumber).Last().Balance;
        Assert.Equal(cash, ledger.Transactions.OrderBy(row => row.Date).ThenBy(row => row.SourceRowNumber).Last().Balance);

        var flags = Evaluate(ledger);
        Assert.Equal(
            [AxumHomeBook.LateCustomer, AxumHomeBook.StoppedCustomer, AxumHomeBook.DroppedCustomer],
            flags.Select(flag => flag.CustomerName).ToArray());
        Assert.Equal([CallFlagKind.Late, CallFlagKind.Stopped, CallFlagKind.Dropped], flags.Select(flag => flag.Kind).ToArray());
        Assert.Contains(AxumHomeBook.LateInvoiceRowId, flags[0].CitedRowIds);
        AssertCitedRows(ledger, flags);
        Assert.Contains(ledger.Customers.SelectMany(customer => customer.Invoices), invoice => invoice.Status == InvoiceStatus.Void);
        Assert.NotEmpty(ledger.CreditNotes);
        Assert.Equal(AxumDemoFigures.Regions.Length, ledger.Customers.Count);
    }

    [Fact]
    public void Axum_constants_match_the_demo_file()
    {
        var root = RepoRoot();
        var text = File.ReadAllText(Path.Combine(root, "docs", "demos", "axum-home", "data.js"));
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        using var doc = JsonDocument.Parse(text[start..(end + 1)]);
        var rootElement = doc.RootElement;
        var nextTwo = rootElement.GetProperty("forecastAll").EnumerateArray().Sum(row => row.GetProperty("forecast_units").GetInt32());
        Assert.Equal(AxumHomeBook.NextTwoDayUnits, nextTwo);

        var skus = new HashSet<string>(StringComparer.Ordinal);
        var regions = new HashSet<string>(StringComparer.Ordinal);
        var models = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rootElement.GetProperty("forecastAll").EnumerateArray())
        {
            var sku = row.GetProperty("sku").GetString()!;
            var region = row.GetProperty("region").GetString()!;
            skus.Add(sku);
            regions.Add(region);
            models.Add(sku + "||" + region);
        }

        Assert.Equal(AxumDemoFigures.Skus.Length, skus.Count);
        Assert.Equal(AxumDemoFigures.Regions.Length, regions.Count);
        Assert.Equal(AxumDemoFigures.ModelCount, models.Count);

        var units = 0;
        foreach (var pair in rootElement.GetProperty("historyByCombo").EnumerateObject())
        {
            foreach (var day in pair.Value.EnumerateArray())
            {
                units += day.GetProperty("units").GetInt32();
            }
        }

        Assert.Equal(AxumDemoFigures.History.Sum(row => row.Units), units);
    }

    [Fact]
    public async Task Each_sample_loads_into_an_empty_business_and_stays_inside_it()
    {
        var studioId = Guid.NewGuid();
        var bakeryId = Guid.NewGuid();
        var studioUserId = Guid.NewGuid();
        var bakeryUserId = Guid.NewGuid();
        var options = Sqlite();
        await using (var setup = new VhonaDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Organizations.AddRange(
                new Organization { Id = studioId, Name = "Harbour Street Studio", CreatedAt = Now },
                new Organization { Id = bakeryId, Name = "Market Bakery", CreatedAt = Now });
            setup.Users.AddRange(
                new AppUser { Id = studioUserId, ExternalId = "studio", Email = "owner@example.com", DisplayName = "Ada", CreatedAt = Now },
                new AppUser { Id = bakeryUserId, ExternalId = "bakery", Email = "baker@example.com", DisplayName = "Bo", CreatedAt = Now });
            await setup.SaveChangesAsync();
        }

        var studio = new Person(studioId, studioUserId, "Owner", "Harbour Street Studio");
        var bakery = new Person(bakeryId, bakeryUserId, "Owner", "Market Bakery");
        await using var studioDb = new VhonaDbContext(options, studio);
        await using var bakeryDb = new VhonaDbContext(options, bakery);
        var clock = new FixedClock(Now);

        var studioCalls = Service(studioDb, studio, clock);
        var unknown = await Assert.ThrowsAsync<InvalidOperationException>(() => studioCalls.LoadSampleAsync("sondela"));
        Assert.Contains("Karoo Kitchen Group", unknown.Message, StringComparison.Ordinal);

        await studioCalls.LoadSampleAsync(SampleBookCatalog.KarooId);
        var again = await Assert.ThrowsAsync<InvalidOperationException>(() => studioCalls.LoadSampleAsync(SampleBookCatalog.AxumId));
        Assert.Contains("no invoices", again.Message, StringComparison.OrdinalIgnoreCase);

        var karoo = await studioCalls.GetListAsync();
        Assert.Equal(KarooKitchenBook.AsAt, karoo.AsAt);
        Assert.Equal("Karoo Kitchen Group", karoo.SampleName);
        Assert.True(karoo.UsingSample);
        Assert.False(karoo.CanLoadSample);
        Assert.Equal(
            [KarooKitchenBook.LateCustomer, KarooKitchenBook.StoppedCustomer, KarooKitchenBook.DroppedCustomer],
            karoo.Flags.Select(flag => flag.CustomerName).ToArray());
        Assert.Contains(KarooKitchenBook.LateInvoiceRowId, karoo.Flags[0].CitedRowIds);

        var late = await studioCalls.GetFlagAsync(karoo.Flags[0].CustomerId);
        Assert.Equal(ReminderDraftTemplate.NotSent, late!.DraftStatus);
        Assert.Contains("KK SH 1842", late.DraftBody);
        Assert.StartsWith("https://wa.me/27825551901?text=", late.WhatsAppLink);
        Assert.StartsWith("mailto:lerato@signalhill.example?", late.EmailLink);

        var health = await new HealthKpiService(studioDb, studio).GetSnapshotAsync();
        Assert.Equal(new DateOnly(2026, 6, 1), health.CurrentPeriod!.Start);
        Assert.Equal(KarooKitchenBook.June2026Revenue, health.Revenue!.CurrentValue);
        Assert.Equal(KarooKitchenBook.May2026Revenue, health.Revenue.PreviousValue);
        Assert.Equal(KarooKitchenBook.June2026Expenses, health.Expenses!.CurrentValue);
        Assert.Equal(KarooKitchenBook.June2026Profit, health.Profit!.CurrentValue);
        Assert.Equal(KarooKitchenBook.June2026Cash, health.Cash!.CurrentValue);
        Assert.Null(health.Cash.MissingNote);
        Assert.True(await studioDb.CreditNotes.AnyAsync());
        Assert.Contains(await studioDb.Invoices.Select(invoice => invoice.Status).ToListAsync(), status => status == InvoiceStatus.Void);
        Assert.Equal(0, await studioDb.Transactions.CountAsync(row => row.Date >= new DateOnly(2026, 7, 1)));

        await Service(bakeryDb, bakery, clock).LoadSampleAsync(SampleBookCatalog.AxumId);
        var axum = await Service(bakeryDb, bakery, clock).GetListAsync();
        Assert.Equal(AxumHomeBook.AsAt, axum.AsAt);
        Assert.Equal("Axum Home", axum.SampleName);
        Assert.Equal(
            [AxumHomeBook.LateCustomer, AxumHomeBook.StoppedCustomer, AxumHomeBook.DroppedCustomer],
            axum.Flags.Select(flag => flag.CustomerName).ToArray());
        Assert.Contains(AxumHomeBook.LateInvoiceRowId, axum.Flags[0].CitedRowIds);
        Assert.DoesNotContain(axum.Flags, flag => flag.CustomerName == KarooKitchenBook.LateCustomer);
        Assert.DoesNotContain(karoo.Flags, flag => flag.CustomerName == AxumHomeBook.LateCustomer);
        Assert.Null(await studioCalls.GetFlagAsync(axum.Flags[0].CustomerId));

        var axumHealth = await new HealthKpiService(bakeryDb, bakery).GetSnapshotAsync();
        Assert.Equal(new DateOnly(2026, 3, 1), axumHealth.CurrentPeriod!.Start);
        Assert.Equal(new DateOnly(2026, 2, 1), axumHealth.PreviousPeriod!.Start);
        var openMonth = axumHealth.OpenMonthNote;
        Assert.NotNull(openMonth);
        Assert.Contains("April 2026 is still open", openMonth);
        Assert.Contains("March 2026", openMonth);
        Assert.Contains("February 2026", openMonth);
        Assert.Equal(openMonth + " ", axumHealth.Revenue!.WhyPrompt[..(openMonth.Length + 1)]);
        Assert.NotNull(axumHealth.Cash);
        Assert.Null(axumHealth.Cash!.MissingNote);
        Assert.Equal(AxumHomeBook.AsAt, axum.AsAt);

        var member = new Person(studioId, Guid.NewGuid(), "Member", "Harbour Street Studio");
        var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(studioDb, member, clock).LoadSampleAsync(SampleBookCatalog.KarooId));
        Assert.Contains("owner", blocked.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sample_copy_does_not_name_a_real_order_system_or_the_old_fixture()
    {
        var root = RepoRoot();
        var calling = Path.Combine(root, "src", "VhonaAI.Core", "Calling");
        var files = new[]
        {
            Path.Combine(calling, "KarooKitchenBook.cs"),
            Path.Combine(calling, "AxumHomeBook.cs"),
            Path.Combine(calling, "AxumDemoFigures.cs"),
            Path.Combine(calling, "SampleLedger.cs"),
            Path.Combine(root, "src", "VhonaAI.Web", "Components", "Pages", "WhoToCall.razor")
        };
        var forbidden = new[] { "Skynamo", "Casa Mia", "Sondela Dental" };
        var hits = files
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => forbidden.Where(token => file.Text.Contains(token, StringComparison.Ordinal)).Select(token => $"{token} in {file.Path}"))
            .ToList();
        Assert.Empty(hits);
        var page = File.ReadAllText(Path.Combine(root, "src", "VhonaAI.Web", "Components", "Pages", "WhoToCall.razor"));
        Assert.Contains("Load Karoo Kitchen Group", page);
        Assert.Contains("Load Axum Home", page);
        Assert.DoesNotContain("Load sample books", page);
    }

    private static void AssertCitedRows(SampleLedger ledger, IReadOnlyList<WhoToCallFlag> flags)
    {
        foreach (var flag in flags)
        {
            var customer = ledger.Customers.Single(item => item.CustomerId == flag.CustomerId);
            var countable = customer.Invoices
                .Where(invoice => WhoToCallRules.IsCountableStatus(invoice.Status))
                .Select(invoice => invoice.RowId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(countable, flag.CitedRowIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
            Assert.NotEmpty(flag.CitedRowIds);
        }
    }

    private static IReadOnlyList<WhoToCallFlag> Evaluate(SampleLedger ledger)
    {
        var result = WhoToCallRules.Evaluate(
            new WhoToCallBooks
            {
                AsAt = LatestCountable(ledger),
                BusinessName = "Harbour Street Studio",
                Customers = ledger.Customers
            },
            WhoToCallThresholds.FromDefaults());
        return result.Flags;
    }

    private static DateOnly LatestCountable(SampleLedger ledger) =>
        ledger.Customers
            .SelectMany(customer => customer.Invoices)
            .Where(invoice => WhoToCallRules.IsCountableStatus(invoice.Status))
            .Max(invoice => invoice.InvoiceDate);

    private static WhoToCallService Service(VhonaDbContext db, Person user, FixedClock clock) =>
        new(db, user, clock, new CallSettingsService(db, user));

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

    private sealed class Person(Guid organizationId, Guid userId, string role, string organizationName) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid UserId { get; } = userId;
        public Guid OrganizationId { get; } = organizationId;
        public string Email => "owner@example.com";
        public string DisplayName => "Ada";
        public string OrganizationName { get; } = organizationName;
        public string Role { get; } = role;
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }
}
