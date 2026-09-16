using System.Globalization;
using CruxAI.Core.Entities;
using CruxAI.Core.Mapping;

namespace CruxAI.Core.Health;

public enum HealthMetricKind
{
    Revenue,
    Expenses,
    Profit,
    Cash
}

public sealed class MonthPeriod
{
    public MonthPeriod(int year, int month)
    {
        Year = year;
        Month = month;
        Start = new DateOnly(year, month, 1);
        EndExclusive = Start.AddMonths(1);
    }

    public int Year { get; }
    public int Month { get; }
    public DateOnly Start { get; }
    public DateOnly EndExclusive { get; }
    public string Label => Start.ToString("MMM yyyy", CultureInfo.GetCultureInfo("en-ZA"));
    public MonthPeriod Previous
    {
        get
        {
            var prior = Start.AddMonths(-1);
            return new MonthPeriod(prior.Year, prior.Month);
        }
    }

    public bool Contains(DateOnly date) => date >= Start && date < EndExclusive;

    public static MonthPeriod FromDate(DateOnly date) => new(date.Year, date.Month);
}

public sealed class MetricCard
{
    public required HealthMetricKind Kind { get; init; }
    public required decimal CurrentValue { get; init; }
    public decimal? PreviousValue { get; init; }
    public decimal? Delta { get; init; }
    public decimal? DeltaPercent { get; init; }
    public string? MissingNote { get; init; }
    public required string WhyPrompt { get; init; }
    public required IReadOnlyList<string> CurrentRowIds { get; init; }
    public required IReadOnlyList<string> PreviousRowIds { get; init; }
}

public sealed class HealthSnapshot
{
    public bool HasData { get; init; }
    public MonthPeriod? CurrentPeriod { get; init; }
    public MonthPeriod? PreviousPeriod { get; init; }
    public MetricCard? Revenue { get; init; }
    public MetricCard? Expenses { get; init; }
    public MetricCard? Profit { get; init; }

    /// <summary>Null means the cash card must be hidden — never treat missing cash as zero.</summary>
    public MetricCard? Cash { get; init; }

    public IReadOnlyList<string> MissingNotes { get; init; } = [];
    public Guid? LatestImportJobId { get; init; }

    public IEnumerable<MetricCard> VisibleCards
    {
        get
        {
            if (Revenue is not null) yield return Revenue;
            if (Expenses is not null) yield return Expenses;
            if (Profit is not null) yield return Profit;
            if (Cash is not null) yield return Cash;
        }
    }

    public MetricCard? Card(HealthMetricKind kind) => kind switch
    {
        HealthMetricKind.Revenue => Revenue,
        HealthMetricKind.Expenses => Expenses,
        HealthMetricKind.Profit => Profit,
        HealthMetricKind.Cash => Cash,
        _ => null
    };
}

public static class HealthKpiCalculator
{
    public static HealthSnapshot Compute(
        IReadOnlyList<Transaction> transactions,
        bool cashFieldMapped,
        Guid? latestImportJobId = null)
    {
        if (transactions.Count == 0)
        {
            return new HealthSnapshot { HasData = false, LatestImportJobId = latestImportJobId };
        }

        var currentPeriod = MonthPeriod.FromDate(transactions.Max(t => t.Date));
        var previousPeriod = currentPeriod.Previous;
        var currentRows = transactions.Where(t => currentPeriod.Contains(t.Date)).ToList();
        var previousRows = transactions.Where(t => previousPeriod.Contains(t.Date)).ToList();
        var notes = new List<string>();

        if (previousRows.Count == 0)
        {
            notes.Add($"No {previousPeriod.Label} transactions to compare.");
        }

        var revenue = BuildFlowCard(
            HealthMetricKind.Revenue,
            currentPeriod,
            previousPeriod,
            currentRows,
            previousRows,
            row => row.Amount > 0,
            row => row.Amount);

        var expenses = BuildFlowCard(
            HealthMetricKind.Expenses,
            currentPeriod,
            previousPeriod,
            currentRows,
            previousRows,
            row => row.Amount < 0,
            row => Math.Abs(row.Amount));

        var profit = BuildTotalsCard(
            HealthMetricKind.Profit,
            currentPeriod,
            previousPeriod,
            revenue.CurrentValue - expenses.CurrentValue,
            previousRows.Count == 0 ? null : revenue.PreviousValue - expenses.PreviousValue,
            currentRows.Select(t => t.RowId).ToList(),
            previousRows.Select(t => t.RowId).ToList(),
            previousRows.Count == 0 ? $"No {previousPeriod.Label} transactions to compare." : null);

        var cash = BuildCashCard(cashFieldMapped, currentPeriod, previousPeriod, currentRows, previousRows);
        if (cashFieldMapped && cash is null)
        {
            notes.Add("Cash is hidden — the mapped balance field has no usable values in this month.");
        }

        if (revenue.MissingNote is not null && !notes.Contains(revenue.MissingNote))
        {
            notes.Add(revenue.MissingNote);
        }

        return new HealthSnapshot
        {
            HasData = true,
            CurrentPeriod = currentPeriod,
            PreviousPeriod = previousPeriod,
            Revenue = revenue,
            Expenses = expenses,
            Profit = profit,
            Cash = cash,
            MissingNotes = notes.Distinct().ToList(),
            LatestImportJobId = latestImportJobId
        };
    }

    public static bool MappingIncludesCashField(IReadOnlyDictionary<string, string>? mapping) =>
        mapping is not null
        && mapping.Values.Any(value => string.Equals(value, TransactionFields.Balance, StringComparison.OrdinalIgnoreCase));

    private static MetricCard BuildFlowCard(
        HealthMetricKind kind,
        MonthPeriod currentPeriod,
        MonthPeriod previousPeriod,
        IReadOnlyList<Transaction> currentRows,
        IReadOnlyList<Transaction> previousRows,
        Func<Transaction, bool> selector,
        Func<Transaction, decimal> value)
    {
        var currentMatch = currentRows.Where(selector).ToList();
        var previousMatch = previousRows.Where(selector).ToList();
        var currentTotal = currentMatch.Sum(value);
        decimal? previousTotal = previousRows.Count == 0 ? null : previousMatch.Sum(value);
        var missing = previousRows.Count == 0
            ? $"No {previousPeriod.Label} transactions to compare."
            : null;

        return BuildTotalsCard(
            kind,
            currentPeriod,
            previousPeriod,
            currentTotal,
            previousTotal,
            currentMatch.Select(t => t.RowId).ToList(),
            previousMatch.Select(t => t.RowId).ToList(),
            missing);
    }

    private static MetricCard? BuildCashCard(
        bool cashFieldMapped,
        MonthPeriod currentPeriod,
        MonthPeriod previousPeriod,
        IReadOnlyList<Transaction> currentRows,
        IReadOnlyList<Transaction> previousRows)
    {
        if (!cashFieldMapped)
        {
            return null;
        }

        var currentCash = LatestBalance(currentRows);
        if (currentCash is null)
        {
            return null;
        }

        var previousCash = LatestBalance(previousRows);
        var missing = previousCash is null
            ? $"No usable {previousPeriod.Label} balance to compare."
            : currentRows.Count(t => !t.Balance.HasValue) > 0
                ? $"Balance missing on {currentRows.Count(t => !t.Balance.HasValue)} of {currentRows.Count} {currentPeriod.Label} row(s)."
                : null;

        var currentRowIds = currentRows.Where(t => t.Balance.HasValue).Select(t => t.RowId).ToList();
        var previousRowIds = previousRows.Where(t => t.Balance.HasValue).Select(t => t.RowId).ToList();

        return BuildTotalsCard(
            HealthMetricKind.Cash,
            currentPeriod,
            previousPeriod,
            currentCash.Value.Balance,
            previousCash?.Balance,
            currentRowIds,
            previousRowIds,
            missing);
    }

    private static (decimal Balance, string RowId)? LatestBalance(IReadOnlyList<Transaction> rows)
    {
        var withBalance = rows
            .Where(t => t.Balance.HasValue)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.SourceRowNumber)
            .LastOrDefault();

        return withBalance is null ? null : (withBalance.Balance!.Value, withBalance.RowId);
    }

    private static MetricCard BuildTotalsCard(
        HealthMetricKind kind,
        MonthPeriod currentPeriod,
        MonthPeriod previousPeriod,
        decimal current,
        decimal? previous,
        IReadOnlyList<string> currentRowIds,
        IReadOnlyList<string> previousRowIds,
        string? missingNote)
    {
        decimal? delta = previous is null ? null : current - previous;
        decimal? percent = previous is null || previous == 0
            ? null
            : decimal.Round(delta!.Value / previous.Value, 4);

        return new MetricCard
        {
            Kind = kind,
            CurrentValue = current,
            PreviousValue = previous,
            Delta = delta,
            DeltaPercent = percent,
            MissingNote = missingNote,
            WhyPrompt = SeedWhyPrompt(kind, currentPeriod, previousPeriod, current, previous),
            CurrentRowIds = currentRowIds,
            PreviousRowIds = previousRowIds
        };
    }

    public static string SeedWhyPrompt(
        HealthMetricKind kind,
        MonthPeriod currentPeriod,
        MonthPeriod previousPeriod,
        decimal current,
        decimal? previous)
    {
        var name = kind.ToString().ToLowerInvariant();
        var currentText = current.ToString("C", CultureInfo.GetCultureInfo("en-ZA"));
        if (previous is null)
        {
            return $"Why is {name} {currentText} in {currentPeriod.Label}?";
        }

        var previousText = previous.Value.ToString("C", CultureInfo.GetCultureInfo("en-ZA"));
        return $"Why did {name} change from {previousText} in {previousPeriod.Label} to {currentText} in {currentPeriod.Label}?";
    }
}
