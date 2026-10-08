using VhonaAI.Core.Calling;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Health;
using VhonaAI.Core.Why;

namespace VhonaAI.Tests;

public class WhyReasonsTests
{
    [Fact]
    public void Reason_amounts_are_the_biggest_named_movements_and_sum_to_the_change()
    {
        var transactions = new List<Transaction>
        {
            Row("may-g", new DateOnly(2026, 5, 12), 400m, "Gardens till takings", "Till takings", "Gardens"),
            Row("may-s", new DateOnly(2026, 5, 12), 300m, "Stellenbosch till takings", "Till takings", "Stellenbosch"),
            Row("jun-g", new DateOnly(2026, 6, 12), 250m, "Gardens till takings", "Till takings", "Gardens"),
            Row("jun-s", new DateOnly(2026, 6, 12), 280m, "Stellenbosch till takings", "Till takings", "Stellenbosch")
        };
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);
        var result = WhyVerifier.Verify(snapshot.Revenue!.WhyPrompt, transactions, snapshot);

        Assert.True(result.Verified);
        Assert.False(result.Held);
        Assert.Equal("Revenue is down R170, 24.3% below May.", result.Reasons.Count == 2 ? FirstLine(result.Answer) : result.Answer);
        Assert.Equal(
            ["Gardens is down R150.", "Stellenbosch is down R20."],
            result.Reasons.Select(reason => reason.Title).ToArray());
        Assert.Equal(snapshot.Revenue.Delta, result.Reasons.Sum(reason => reason.Amount));
        Assert.Equal(["jun-g", "may-g"], result.Reasons[0].RowIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(result.Reasons, reason => reason.Remainder);
        Assert.DoesNotContain("score", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rank", result.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_small_remainder_is_one_everything_else_line_with_its_rows()
    {
        var transactions = new List<Transaction>
        {
            Row("a", new DateOnly(2026, 3, 4), 100m, "North order", "Sales", "North"),
            Row("b", new DateOnly(2026, 3, 4), 40m, "South order", "Sales", "South"),
            Row("c", new DateOnly(2026, 3, 4), 30m, "East order", "Sales", "East"),
            Row("d", new DateOnly(2026, 3, 4), 5m, "West order", "Sales", "West"),
            Row("prev", new DateOnly(2026, 2, 4), 10m, "North order", "Sales", "North")
        };
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);
        var result = WhyVerifier.Verify(snapshot.Revenue!.WhyPrompt, transactions, snapshot);

        Assert.True(result.Verified);
        Assert.Equal(4, result.Reasons.Count);
        Assert.Equal(WhyMessages.EverythingElse, result.Reasons[^1].Name);
        Assert.True(result.Reasons[^1].Remainder);
        Assert.Equal(["d"], result.Reasons[^1].RowIds);
        Assert.Equal(5m, result.Reasons[^1].Amount);
        Assert.Contains("Everything else is up R5.", result.Answer);
        Assert.Equal(snapshot.Revenue.Delta, result.Reasons.Sum(reason => reason.Amount));
    }

    [Fact]
    public void Held_when_the_rows_do_not_add_up_to_the_change()
    {
        var transactions = new List<Transaction>
        {
            Row("feb", new DateOnly(2026, 2, 28), -10m, "Fees", "Bank fees", "Bank", 1_000m),
            Row("mar", new DateOnly(2026, 3, 18), 50m, "Deposit", "Sales", "North", 2_000m)
        };
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: true);

        var result = WhyVerifier.Verify(snapshot.Cash!.WhyPrompt, transactions, snapshot);

        Assert.True(result.Held);
        Assert.False(result.Verified);
        Assert.Equal(WhyMessages.Held, result.Answer);
        Assert.Empty(result.Reasons);
        Assert.Contains("feb", result.Citations.Select(citation => citation.RowId));
        Assert.Contains("mar", result.Citations.Select(citation => citation.RowId));
        Assert.DoesNotContain("R2 000", result.Answer);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void Held_when_a_cited_row_is_missing_from_the_saved_rows()
    {
        var transactions = new List<Transaction>
        {
            Row("may", new DateOnly(2026, 5, 8), 200m, "Till", "Till takings", "Gardens"),
            Row("jun", new DateOnly(2026, 6, 8), 80m, "Till", "Till takings", "Gardens")
        };
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);
        var kept = transactions.Where(row => row.RowId != "jun").ToList();

        var result = WhyVerifier.Verify(snapshot.Revenue!.WhyPrompt, kept, snapshot);

        Assert.True(result.Held);
        Assert.Empty(result.Reasons);
        Assert.Equal(WhyMessages.Held, result.Answer);
    }

    [Fact]
    public void An_open_month_explains_the_last_complete_month_only()
    {
        var transactions = new List<Transaction>
        {
            Row("feb", new DateOnly(2026, 2, 10), 4_000m, "February order", "Sales", "North"),
            Row("mar", new DateOnly(2026, 3, 10), 5_000m, "March order", "Sales", "North"),
            Row("apr", new DateOnly(2026, 4, 5), 100m, "April order", "Sales", "North")
        };
        var snapshot = HealthKpiCalculator.Compute(
            transactions,
            cashFieldMapped: false,
            booksAsAt: new DateOnly(2026, 4, 13));

        Assert.Equal(new DateOnly(2026, 3, 1), snapshot.CurrentPeriod!.Start);
        Assert.Equal(new DateOnly(2026, 2, 1), snapshot.PreviousPeriod!.Start);
        Assert.Contains("April 2026 is still open, up to 13 April", snapshot.OpenMonthNote);
        Assert.Contains("March 2026", snapshot.OpenMonthNote);
        Assert.Contains("February 2026", snapshot.OpenMonthNote);

        var result = WhyVerifier.Verify(snapshot.Revenue!.WhyPrompt, transactions, snapshot);

        Assert.True(result.Verified);
        Assert.Equal("Revenue is up R1 000, 25% above February.", FirstLine(result.Answer));
        Assert.DoesNotContain("apr", result.Citations.Select(citation => citation.RowId));
        Assert.DoesNotContain("is still open", result.Answer, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(File.ReadAllText(WhyPage()), "<p class=\"hint\">"));
    }

    [Fact]
    public void Karoo_revenue_why_names_the_three_branches()
    {
        var transactions = KarooKitchenBook.Build().Transactions.Select(ToTransaction).ToList();
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: true, booksAsAt: KarooKitchenBook.AsAt);
        var result = WhyVerifier.Verify(snapshot.Revenue!.WhyPrompt, transactions, snapshot);

        Assert.True(result.Verified);
        Assert.Equal("Revenue is down R70 800, 8.7% below May.", FirstLine(result.Answer));
        Assert.Equal(
            ["Gardens is down R30 436.", "Stellenbosch is down R23 478.", "Sea Point is down R16 886."],
            result.Reasons.Select(reason => reason.Title).ToArray());
        Assert.Equal(snapshot.Revenue.Delta, result.Reasons.Sum(reason => reason.Amount));
        Assert.DoesNotContain(result.Reasons, reason => reason.Name == WhyMessages.EverythingElse);
    }

    [Fact]
    public void Karoo_profit_why_puts_the_smaller_movement_on_everything_else()
    {
        var transactions = KarooKitchenBook.Build().Transactions.Select(ToTransaction).ToList();
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: true, booksAsAt: KarooKitchenBook.AsAt);
        var result = WhyVerifier.Verify(snapshot.Profit!.WhyPrompt, transactions, snapshot);

        Assert.True(result.Verified);
        Assert.Contains("below May.", FirstLine(result.Answer));
        Assert.Equal(WhyMessages.EverythingElse, result.Reasons[^1].Name);
        Assert.NotEmpty(result.Reasons[^1].RowIds);
        Assert.Equal(snapshot.Profit.Delta, result.Reasons.Sum(reason => reason.Amount));
    }

    private static string FirstLine(string answer) =>
        answer.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];

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

    private static string WhyPage()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "VhonaAI.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "src", "VhonaAI.Web", "Components", "Pages", "Why.razor");
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
