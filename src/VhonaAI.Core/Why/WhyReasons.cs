using System.Globalization;
using System.Text;
using VhonaAI.Core.Calling;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Health;

namespace VhonaAI.Core.Why;

/// <summary>
/// Splits a verified month on month change into the largest named movements.
/// Names come from the counterparty, then the category, then the description.
/// A grouping is used only when the rows left over are smaller than each shown reason
/// and no more than a third of the change.
/// </summary>
public static class WhyReasons
{
    public const int MaxNamedReasons = 3;

    public static string Headline(string label, decimal current, decimal previous, MonthPeriod previousPeriod)
    {
        var delta = current - previous;
        var month = RandAmounts.MonthName(previousPeriod.Month);
        if (delta == 0)
        {
            return $"{label} is unchanged from {month}.";
        }

        var direction = delta > 0 ? "up" : "down";
        var relation = delta > 0 ? "above" : "below";
        var money = RandAmounts.Format(Math.Abs(delta));
        if (previous == 0)
        {
            return $"{label} is {direction} {money}.";
        }

        var percent = decimal.Round(delta / previous, 4);
        var pctText = Math.Abs(percent * 100m).ToString("0.#", CultureInfo.InvariantCulture);
        return $"{label} is {direction} {money}, {pctText}% {relation} {month}.";
    }

    public static WhyExplanation Explain(
        HealthMetricKind metric,
        MonthPeriod currentPeriod,
        MonthPeriod previousPeriod,
        decimal cardCurrent,
        decimal cardPrevious,
        IReadOnlyList<Transaction> currentRows,
        IReadOnlyList<Transaction> previousRows)
    {
        var headline = Headline(Label(metric), cardCurrent, cardPrevious, previousPeriod);
        var delta = cardCurrent - cardPrevious;
        if (!RowsMatchCard(metric, currentRows, previousRows, cardCurrent, cardPrevious, delta))
        {
            return WhyExplanation.Hold(headline, WhyMessages.Held);
        }

        var reasons = ChooseReasons(metric, currentRows, previousRows, delta);
        if (reasons is null)
        {
            return WhyExplanation.Hold(headline, WhyMessages.HeldUnexplained);
        }

        return WhyExplanation.Shown(headline, reasons);
    }

    public static IReadOnlyList<WhyReasonLine> Rebuild(HealthMetricKind metric, IReadOnlyList<WhyCitedRow> cited)
    {
        if (cited.Count == 0)
        {
            return [];
        }

        var culture = CultureInfo.GetCultureInfo("en-ZA");
        var dated = new List<(DateTime Period, WhyCitedRow Row)>();
        foreach (var row in cited)
        {
            if (string.IsNullOrWhiteSpace(row.PeriodLabel)
                || !DateTime.TryParseExact(row.PeriodLabel, "MMM yyyy", culture, DateTimeStyles.None, out var period))
            {
                return [];
            }

            dated.Add((period, row));
        }

        var currentStamp = dated.Max(item => item.Period);
        var currentRows = dated.Where(item => item.Period == currentStamp).Select(item => ToTransaction(item.Row)).ToList();
        var previousRows = dated.Where(item => item.Period != currentStamp).Select(item => ToTransaction(item.Row)).ToList();
        if (currentRows.Count == 0)
        {
            return [];
        }

        var currentValue = Measure(metric, currentRows);
        var previousValue = previousRows.Count == 0 ? 0 : Measure(metric, previousRows);
        if (currentValue is null || previousValue is null)
        {
            return [];
        }

        var currentPeriod = MonthPeriod.FromDate(DateOnly.FromDateTime(currentStamp));
        var previousPeriod = previousRows.Count == 0
            ? currentPeriod.Previous
            : MonthPeriod.FromDate(DateOnly.FromDateTime(dated.Where(item => item.Period != currentStamp).Min(item => item.Period)));
        var explanation = Explain(metric, currentPeriod, previousPeriod, currentValue.Value, previousValue.Value, currentRows, previousRows);
        return explanation.Held ? [] : explanation.Reasons;
    }

    private static bool RowsMatchCard(
        HealthMetricKind metric,
        IReadOnlyList<Transaction> currentRows,
        IReadOnlyList<Transaction> previousRows,
        decimal cardCurrent,
        decimal cardPrevious,
        decimal delta)
    {
        var current = Measure(metric, currentRows);
        var previous = Measure(metric, previousRows);
        if (current is null || previous is null || current.Value != cardCurrent || previous.Value != cardPrevious)
        {
            return false;
        }

        if (metric == HealthMetricKind.Cash && currentRows.Sum(row => row.Amount) != delta)
        {
            return false;
        }

        return true;
    }

    private static decimal? Measure(HealthMetricKind metric, IReadOnlyList<Transaction> rows)
    {
        if (rows.Count == 0)
        {
            return metric == HealthMetricKind.Cash ? null : 0;
        }

        return metric switch
        {
            HealthMetricKind.Revenue => rows.Sum(row => row.Amount),
            HealthMetricKind.Expenses => rows.Sum(row => Math.Abs(row.Amount)),
            HealthMetricKind.Profit => rows.Sum(row => row.Amount),
            HealthMetricKind.Cash => rows
                .Where(row => row.Balance.HasValue)
                .OrderBy(row => row.Date)
                .ThenBy(row => row.SourceRowNumber)
                .LastOrDefault()
                ?.Balance,
            _ => null
        };
    }

    private static List<WhyReasonLine>? ChooseReasons(
        HealthMetricKind metric,
        IReadOnlyList<Transaction> currentRows,
        IReadOnlyList<Transaction> previousRows,
        decimal delta)
    {
        Func<Transaction, string?>[] levels =
        [
            row => row.Counterparty,
            row => row.Category,
            row => row.Description
        ];

        foreach (var level in levels)
        {
            var buckets = Group(metric, currentRows, previousRows, level);
            if (buckets is null)
            {
                continue;
            }

            var reasons = TryLevel(buckets, delta);
            if (reasons is not null)
            {
                return reasons;
            }
        }

        return null;
    }

    private static List<WhyReasonLine>? TryLevel(List<Bucket> buckets, decimal delta)
    {
        var ordered = buckets
            .Where(bucket => bucket.Amount != 0)
            .OrderByDescending(bucket => Math.Abs(bucket.Amount))
            .ThenBy(bucket => bucket.Name, StringComparer.Ordinal)
            .ToList();
        if (ordered.Count == 0)
        {
            return delta == 0 ? [] : null;
        }

        var head = ordered.Take(MaxNamedReasons).ToList();
        var rest = ordered.Skip(MaxNamedReasons).ToList();
        var reasons = head.Select(bucket => Line(bucket.Name, bucket.Amount, bucket.RowIds, remainder: false)).ToList();
        var remainder = rest.Sum(bucket => bucket.Amount);
        if (remainder != 0)
        {
            if (!RemainderFits(remainder, head, delta))
            {
                return null;
            }

            var rowIds = rest.SelectMany(bucket => bucket.RowIds).Distinct(StringComparer.Ordinal).ToList();
            reasons.Add(Line(WhyMessages.EverythingElse, remainder, rowIds, remainder: true));
        }

        return reasons.Sum(reason => reason.Amount) == delta ? reasons : null;
    }

    private static bool RemainderFits(decimal remainder, List<Bucket> shown, decimal delta)
    {
        var smallest = shown.Min(bucket => Math.Abs(bucket.Amount));
        if (Math.Abs(remainder) >= smallest)
        {
            return false;
        }

        if (delta == 0)
        {
            return false;
        }

        return Math.Abs(remainder) <= Math.Abs(delta) / 3m;
    }

    private static List<Bucket>? Group(
        HealthMetricKind metric,
        IReadOnlyList<Transaction> currentRows,
        IReadOnlyList<Transaction> previousRows,
        Func<Transaction, string?> key)
    {
        var buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        if (!Add(buckets, metric, metric == HealthMetricKind.Cash ? [] : previousRows, key, current: false))
        {
            return null;
        }

        if (!Add(buckets, metric, currentRows, key, current: true))
        {
            return null;
        }

        return buckets.Values.ToList();
    }

    private static bool Add(
        Dictionary<string, Bucket> buckets,
        HealthMetricKind metric,
        IReadOnlyList<Transaction> rows,
        Func<Transaction, string?> key,
        bool current)
    {
        foreach (var row in rows)
        {
            var name = key(row)?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (!buckets.TryGetValue(name, out var bucket))
            {
                bucket = new Bucket(name);
                buckets.Add(name, bucket);
            }

            var signed = Signed(metric, row);
            if (current)
            {
                bucket.Current += signed;
            }
            else
            {
                bucket.Previous += signed;
            }

            bucket.RowIds.Add(row.RowId);
        }

        return true;
    }

    private static decimal Signed(HealthMetricKind metric, Transaction row) =>
        metric == HealthMetricKind.Expenses ? Math.Abs(row.Amount) : row.Amount;

    private static WhyReasonLine Line(string name, decimal amount, IReadOnlyList<string> rowIds, bool remainder) => new()
    {
        Name = name,
        Amount = amount,
        Remainder = remainder,
        Title = Sentence(name, amount),
        RowIds = rowIds
    };

    private static string Sentence(string name, decimal amount)
    {
        if (amount > 0)
        {
            return $"{name} is up {RandAmounts.Format(amount)}.";
        }

        if (amount < 0)
        {
            return $"{name} is down {RandAmounts.Format(Math.Abs(amount))}.";
        }

        return $"{name} is unchanged.";
    }

    private static string Label(HealthMetricKind metric) => metric switch
    {
        HealthMetricKind.Revenue => "Revenue",
        HealthMetricKind.Expenses => "Expenses",
        HealthMetricKind.Profit => "Profit",
        HealthMetricKind.Cash => "Cash",
        _ => metric.ToString()
    };

    private static Transaction ToTransaction(WhyCitedRow row) => new()
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

    private sealed class Bucket(string name)
    {
        public string Name { get; } = name;
        public decimal Current { get; set; }
        public decimal Previous { get; set; }
        public decimal Amount => Current - Previous;
        public List<string> RowIds { get; } = [];
    }
}

public sealed class WhyExplanation
{
    public required bool Held { get; init; }
    public required string Headline { get; init; }
    public required string Answer { get; init; }
    public required IReadOnlyList<WhyReasonLine> Reasons { get; init; }

    public static WhyExplanation Hold(string headline, string reason) => new()
    {
        Held = true,
        Headline = headline,
        Answer = reason,
        Reasons = []
    };

    public static WhyExplanation Shown(string headline, IReadOnlyList<WhyReasonLine> reasons)
    {
        var text = new StringBuilder(headline);
        foreach (var reason in reasons)
        {
            text.AppendLine();
            text.AppendLine();
            text.Append(reason.Title);
        }

        return new WhyExplanation
        {
            Held = false,
            Headline = headline,
            Answer = text.ToString(),
            Reasons = reasons
        };
    }
}
