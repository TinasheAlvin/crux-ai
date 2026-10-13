using VhonaAI.Core.Calling;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Health;
using VhonaAI.Core.Why;

namespace VhonaAI.Tests;

public class WhyScreenTests
{
    [Fact]
    public void The_screen_headline_names_both_months_and_keeps_the_amount_out_of_a_sentence()
    {
        var june = new MonthPeriod(2026, 6);
        var may = new MonthPeriod(2026, 5);
        var march = new MonthPeriod(2026, 3);
        var february = new MonthPeriod(2026, 2);

        Assert.Equal("Revenue down R70 800 in June vs May", WhyScreen.Headline("Revenue", 741_200m, 812_000m, june, may));
        Assert.Equal("June revenue was R741 200. May was R812 000.", WhyScreen.Lede("Revenue", 741_200m, 812_000m, june, may));
        Assert.Equal("Counted to 30 June 2026", WhyScreen.CountedTo(june));
        Assert.Equal("Revenue up R353 424 in March vs February", WhyScreen.Headline("Revenue", 5_890_390m, 5_536_966m, march, february));
        Assert.Equal("Counted to 31 March 2026", WhyScreen.CountedTo(march));
        Assert.Equal(
            "June expenses were R655 100. May was R636 700.",
            WhyScreen.Lede("Expenses", 655_100m, 636_700m, june, may));
        Assert.Equal("Less than May", WhyScreen.MovementHeading(-70_800m, may));
        Assert.Equal("More than February", WhyScreen.MovementHeading(353_424m, february));
    }

    [Fact]
    public void A_reason_line_keeps_the_name_and_the_amount_apart()
    {
        var down = new WhyReasonLine
        {
            Name = "Gardens",
            Title = "Gardens is down R30 436.",
            Amount = -30_436m,
            Remainder = false,
            RowIds = ["a", "b"]
        };
        var rest = new WhyReasonLine
        {
            Name = WhyMessages.EverythingElse,
            Title = "Everything else is up R5.",
            Amount = 5m,
            Remainder = true,
            RowIds = ["c"]
        };

        Assert.Equal("Gardens", WhyScreen.PlainLabel(down));
        Assert.DoesNotContain("R", WhyScreen.PlainLabel(down), StringComparison.Ordinal);
        Assert.Equal("Down", WhyScreen.Context(down));
        Assert.Equal("1 small change", WhyScreen.Context(rest));
        Assert.Equal("Three reasons and everything else", WhyScreen.AddsUpCaption(3, true));
        Assert.Equal("Two reasons", WhyScreen.AddsUpCaption(2, false));
    }

    [Fact]
    public void Open_rows_group_by_month_and_the_difference_equals_the_reason()
    {
        var transactions = KarooKitchenBook.Build().Transactions.Select(ToTransaction).ToList();
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: true, booksAsAt: KarooKitchenBook.AsAt);
        var result = WhyVerifier.Verify(snapshot.Revenue!.WhyPrompt, transactions, snapshot);
        var reason = result.Reasons[0];
        var cited = Cite(transactions, reason.RowIds);

        var sheet = WhyScreen.Rows(
            HealthMetricKind.Revenue,
            reason,
            cited,
            snapshot.CurrentPeriod!,
            snapshot.PreviousPeriod!);

        Assert.Equal(reason.Amount, sheet.Difference);
        Assert.Equal(sheet.Current.Total - sheet.Previous.Total, sheet.Difference);
        Assert.Equal("June", sheet.Current.Name);
        Assert.Equal("May", sheet.Previous.Name);
        Assert.Equal(reason.RowIds.Count, sheet.Chips.Count);
        Assert.Equal(Math.Max(0, reason.RowIds.Count - WhyScreen.VisibleChips), sheet.MoreCount);
        Assert.Equal(WhyScreen.VisibleChips, sheet.Chips.Count(chip => !chip.Extra));
        Assert.Contains("grouped June then May", sheet.Caption);
        Assert.DoesNotContain("R", sheet.Caption, StringComparison.Ordinal);
    }

    [Fact]
    public void Held_cash_shows_the_gap_between_the_balance_and_the_rows()
    {
        var transactions = new List<Transaction>
        {
            Row("feb", new DateOnly(2026, 2, 28), -10m, "Fees", "Bank fees", "Bank", 1_000m),
            Row("mar", new DateOnly(2026, 3, 18), 50m, "Deposit", "Sales", "North", 2_000m)
        };
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: true);
        var result = WhyVerifier.Verify(snapshot.Cash!.WhyPrompt, transactions, snapshot);
        var sheet = WhyScreen.Held(
            HealthMetricKind.Cash,
            result.Answer,
            snapshot.Cash.CurrentValue,
            snapshot.Cash.PreviousValue!.Value,
            Cite(transactions, transactions.Select(row => row.RowId)),
            snapshot.CurrentPeriod!,
            snapshot.PreviousPeriod!);

        Assert.Equal(WhyMessages.Held, result.Answer);
        Assert.Equal("Your books say cash went up", sheet.BooksLabel);
        Assert.Equal("R1 000", sheet.BooksAmount);
        Assert.Equal("The rows Vhona can see add up to", sheet.RowsLabel);
        Assert.Equal("R50", sheet.RowsAmount);
        Assert.Equal("With no rows behind it", sheet.GapLabel);
        Assert.Equal("R950", sheet.GapAmount);
        Assert.DoesNotContain("City of Cape Town", sheet.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("Xero", sheet.Title, StringComparison.Ordinal);
        Assert.Contains("do not match", string.Join(" ", sheet.Checks), StringComparison.Ordinal);
    }

    [Fact]
    public void Held_when_the_spread_is_too_wide_names_the_real_remainder()
    {
        var transactions = new List<Transaction>();
        for (var i = 0; i < 6; i++)
        {
            transactions.Add(Row($"feb-{i}", new DateOnly(2026, 2, 4), 10m, $"Line {i}", $"Cat {i}", $"Shop {i}"));
            transactions.Add(Row($"mar-{i}", new DateOnly(2026, 3, 4), 110m, $"Line {i}", $"Cat {i}", $"Shop {i}"));
        }

        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);
        var result = WhyVerifier.Verify(snapshot.Revenue!.WhyPrompt, transactions, snapshot);
        var spread = WhyReasons.UnexplainedSpread(
            HealthMetricKind.Revenue,
            transactions.Where(row => row.Date.Month == 3).ToList(),
            transactions.Where(row => row.Date.Month == 2).ToList(),
            snapshot.Revenue!.Delta!.Value);

        Assert.Equal(WhyMessages.HeldUnexplained, result.Answer);
        Assert.NotNull(spread);
        Assert.Equal(300m, spread.Value.Remainder);
        Assert.Equal(100m, spread.Value.Smallest);
        Assert.True(Math.Abs(spread.Value.Remainder) > spread.Value.Smallest);

        var sheet = WhyScreen.Held(
            HealthMetricKind.Revenue,
            result.Answer,
            snapshot.Revenue.CurrentValue,
            snapshot.Revenue.PreviousValue!.Value,
            Cite(transactions, transactions.Select(row => row.RowId)),
            snapshot.CurrentPeriod!,
            snapshot.PreviousPeriod!);
        Assert.Contains("Everything else would be R300, bigger than the smallest reason.", sheet.Title);
        Assert.Equal("R300", sheet.RowsAmount);
        Assert.Equal("R100", sheet.GapAmount);
        Assert.Equal("Smallest reason", sheet.GapLabel);
    }

    [Fact]
    public void The_why_page_uses_the_mock_structure()
    {
        var page = File.ReadAllText(Source("src", "VhonaAI.Web", "Components", "Pages", "Why.razor"));
        var card = File.ReadAllText(Source("src", "VhonaAI.Web", "Components", "Shared", "WhyReasonCard.razor"));
        var screen = page + card;

        Assert.Contains("<p class=\"eyebrow\">Why</p>", page);
        Assert.Contains("why-rule", page);
        Assert.DoesNotContain("receipted why", screen, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOf(page, "<p class=\"hint\">"));
        Assert.Contains("@CountedTo", page);
        Assert.Contains("Counted to", File.ReadAllText(Source("src", "VhonaAI.Core", "Why", "WhyScreen.cs")));
        Assert.Contains("See the rows", card);
        Assert.Contains("Hide the rows", card);
        Assert.Contains("Adds up to the figure", card);
        Assert.Contains("why-chip", card);
        Assert.Contains("extra", card);
        Assert.Contains("and @sheet.MoreCount more", card);
        Assert.Contains("What to check", page);
        Assert.Contains("With no rows behind it", File.ReadAllText(Source("src", "VhonaAI.Core", "Why", "WhyScreen.cs")));
        Assert.DoesNotContain("why-reason-rows", screen, StringComparison.Ordinal);
        Assert.DoesNotContain("@reason.Title", screen, StringComparison.Ordinal);
        var trust = page.IndexOf("<TrustPrompt", StringComparison.Ordinal);
        var question = page.IndexOf("why-question", StringComparison.Ordinal);
        var saved = page.IndexOf("Saved questions", StringComparison.Ordinal);
        var answer = page.IndexOf("WhyReasonCard", StringComparison.Ordinal);
        Assert.True(answer >= 0 && trust > answer && question > trust && saved > question);
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "VhonaAI.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    private static IReadOnlyList<WhyCitedRow> Cite(IEnumerable<Transaction> transactions, IEnumerable<string> rowIds)
    {
        var wanted = rowIds.ToHashSet(StringComparer.Ordinal);
        return transactions.Where(row => wanted.Contains(row.RowId)).Select(row => new WhyCitedRow
        {
            RowId = row.RowId,
            Columns = "Date, Description, Amount",
            PeriodLabel = MonthPeriod.FromDate(row.Date).Label,
            Date = row.Date,
            Description = row.Description,
            Amount = row.Amount,
            Category = row.Category,
            Counterparty = row.Counterparty,
            Balance = row.Balance,
            SourceRowNumber = row.SourceRowNumber
        }).ToList();
    }

    private static Transaction ToTransaction(SampleBankRow row) => new()
    {
        RowId = row.RowId,
        Date = row.Date,
        Description = row.Description,
        Amount = row.Amount,
        Category = row.Category,
        Counterparty = row.Counterparty,
        Balance = row.Balance,
        SourceRowNumber = row.SourceRowNumber
    };

    private static Transaction Row(
        string rowId,
        DateOnly date,
        decimal amount,
        string description,
        string? category = null,
        string? counterparty = null,
        decimal? balance = null) => new()
    {
        RowId = rowId,
        Date = date,
        Amount = amount,
        Description = description,
        Category = category,
        Counterparty = counterparty,
        Balance = balance,
        SourceRowNumber = 1
    };
}
