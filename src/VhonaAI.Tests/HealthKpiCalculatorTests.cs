using VhonaAI.Core.Entities;
using VhonaAI.Core.Health;
using VhonaAI.Core.Mapping;

namespace VhonaAI.Tests;

public class HealthKpiCalculatorTests
{
    [Fact]
    public void Compares_this_month_to_the_previous_month()
    {
        var transactions = new List<Transaction>
        {
            Row("r-feb-in", new DateOnly(2026, 2, 3), 2800m),
            Row("r-feb-out", new DateOnly(2026, 2, 10), -2000m),
            Row("r-mar-in", new DateOnly(2026, 3, 2), 3500m),
            Row("r-mar-out", new DateOnly(2026, 3, 8), -2700m)
        };

        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);

        Assert.True(snapshot.HasData);
        Assert.Equal("Mar 2026", snapshot.CurrentPeriod!.Label);
        Assert.Equal("Feb 2026", snapshot.PreviousPeriod!.Label);

        Assert.Equal(3500m, snapshot.Revenue!.CurrentValue);
        Assert.Equal(2800m, snapshot.Revenue.PreviousValue);
        Assert.Equal(700m, snapshot.Revenue.Delta);

        Assert.Equal(2700m, snapshot.Expenses!.CurrentValue);
        Assert.Equal(2000m, snapshot.Expenses.PreviousValue);
        Assert.Equal(700m, snapshot.Expenses.Delta);

        Assert.Equal(800m, snapshot.Profit!.CurrentValue);
        Assert.Equal(800m, snapshot.Profit.PreviousValue);
        Assert.Equal(0m, snapshot.Profit.Delta);

        Assert.Contains("r-mar-in", snapshot.Revenue.CurrentRowIds);
        Assert.Contains("r-feb-in", snapshot.Revenue.PreviousRowIds);
        Assert.Contains("Why did revenue change", snapshot.Revenue.WhyPrompt);
    }

    [Fact]
    public void Shows_partial_metrics_when_previous_month_is_missing()
    {
        var transactions = new[]
        {
            Row("r1", new DateOnly(2026, 3, 2), 1200m),
            Row("r2", new DateOnly(2026, 3, 4), -400m)
        };

        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);

        Assert.Equal(1200m, snapshot.Revenue!.CurrentValue);
        Assert.Null(snapshot.Revenue.PreviousValue);
        Assert.Null(snapshot.Revenue.Delta);
        Assert.Equal(800m, snapshot.Profit!.CurrentValue);
        Assert.Contains("No Feb 2026 transactions to compare.", snapshot.MissingNotes);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Revenue.WhyPrompt));
        Assert.Null(snapshot.Cash);
    }

    [Fact]
    public void Hides_cash_when_balance_is_not_mapped()
    {
        var transactions = new[]
        {
            Row("r1", new DateOnly(2026, 3, 2), 100m, balance: 5000m),
            Row("r2", new DateOnly(2026, 3, 5), -40m, balance: 4960m)
        };

        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);

        Assert.Null(snapshot.Cash);
        Assert.DoesNotContain(snapshot.VisibleCards, card => card.Kind == HealthMetricKind.Cash);
        Assert.Equal(100m, snapshot.Revenue!.CurrentValue);
    }

    [Fact]
    public void Hides_cash_when_mapped_but_no_usable_balance_values()
    {
        var transactions = new[]
        {
            Row("r1", new DateOnly(2026, 3, 2), 100m, balance: null),
            Row("r2", new DateOnly(2026, 3, 5), -40m, balance: null)
        };

        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: true);

        Assert.Null(snapshot.Cash);
        Assert.Contains(snapshot.MissingNotes, note => note.Contains("Cash is hidden", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(100m, snapshot.Revenue!.CurrentValue);
        Assert.DoesNotContain(snapshot.VisibleCards, card => card.Kind == HealthMetricKind.Cash);
    }

    [Fact]
    public void Shows_cash_from_latest_usable_balance_and_compares_periods()
    {
        var transactions = new[]
        {
            Row("r-feb", new DateOnly(2026, 2, 28), -50m, balance: 12_000m),
            Row("r-mar-1", new DateOnly(2026, 3, 2), 100m, balance: 12_100m),
            Row("r-mar-2", new DateOnly(2026, 3, 20), -300m, balance: 11_800m)
        };

        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: true);

        Assert.NotNull(snapshot.Cash);
        Assert.Equal(11_800m, snapshot.Cash!.CurrentValue);
        Assert.Equal(12_000m, snapshot.Cash.PreviousValue);
        Assert.Equal(-200m, snapshot.Cash.Delta);
        Assert.Contains("r-mar-2", snapshot.Cash.CurrentRowIds);
        Assert.Contains("Why did cash change", snapshot.Cash.WhyPrompt);
    }

    [Fact]
    public void Never_emits_a_fake_zero_cash_card()
    {
        var withoutField = HealthKpiCalculator.Compute(
            [Row("r1", new DateOnly(2026, 3, 1), 0m)],
            cashFieldMapped: false);
        var mappedButEmpty = HealthKpiCalculator.Compute(
            [Row("r1", new DateOnly(2026, 3, 1), 250m)],
            cashFieldMapped: true);

        Assert.Null(withoutField.Cash);
        Assert.Null(mappedButEmpty.Cash);
        Assert.Equal(0m, withoutField.Revenue!.CurrentValue);
    }

    [Fact]
    public void An_open_as_at_month_compares_the_last_full_month()
    {
        var transactions = new List<Transaction>
        {
            Row("feb-in", new DateOnly(2026, 2, 12), 4000m, balance: 14000m),
            Row("mar-in", new DateOnly(2026, 3, 12), 5000m, balance: 19000m),
            Row("apr-in", new DateOnly(2026, 4, 8), 900m, balance: 19900m)
        };

        var snapshot = HealthKpiCalculator.Compute(
            transactions,
            cashFieldMapped: true,
            booksAsAt: new DateOnly(2026, 4, 13));

        Assert.Equal("Mar 2026", snapshot.CurrentPeriod!.Label);
        Assert.Equal("Feb 2026", snapshot.PreviousPeriod!.Label);
        Assert.Equal(5000m, snapshot.Revenue!.CurrentValue);
        Assert.Equal(4000m, snapshot.Revenue.PreviousValue);
        Assert.Equal(19000m, snapshot.Cash!.CurrentValue);
        Assert.Contains("April 2026 is still open, up to 13 April", snapshot.OpenMonthNote);
        Assert.Contains("These cards compare March 2026 with February 2026", snapshot.OpenMonthNote);
        Assert.Equal("Why did revenue change from R4 000 in Feb 2026 to R5 000 in Mar 2026?", snapshot.Revenue.WhyPrompt);
        Assert.DoesNotContain("is still open", snapshot.Revenue.WhyPrompt);

        var closed = HealthKpiCalculator.Compute(
            transactions,
            cashFieldMapped: false,
            booksAsAt: new DateOnly(2026, 4, 30));
        Assert.Equal("Apr 2026", closed.CurrentPeriod!.Label);
        Assert.Null(closed.OpenMonthNote);

        var laterBooks = HealthKpiCalculator.Compute(
            transactions.Take(2).ToList(),
            cashFieldMapped: false,
            booksAsAt: new DateOnly(2026, 7, 13));
        Assert.Equal("Mar 2026", laterBooks.CurrentPeriod!.Label);
        Assert.Null(laterBooks.OpenMonthNote);
    }

    [Fact]
    public void MappingIncludesCashField_requires_balance()
    {
        var withBalance = new Dictionary<string, string>
        {
            ["Txn Date"] = TransactionFields.Date,
            ["Balance"] = TransactionFields.Balance
        };
        var without = new Dictionary<string, string>
        {
            ["Txn Date"] = TransactionFields.Date,
            ["ZAR Amount"] = TransactionFields.Amount
        };

        Assert.True(HealthKpiCalculator.MappingIncludesCashField(withBalance));
        Assert.False(HealthKpiCalculator.MappingIncludesCashField(without));
        Assert.False(HealthKpiCalculator.MappingIncludesCashField(null));
    }

    private static Transaction Row(string rowId, DateOnly date, decimal amount, decimal? balance = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            RowId = rowId,
            Date = date,
            Amount = amount,
            Description = rowId,
            Balance = balance,
            SourceRowNumber = 1
        };
}
