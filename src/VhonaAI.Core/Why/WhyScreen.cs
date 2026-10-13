using VhonaAI.Core.Calling;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Health;

namespace VhonaAI.Core.Why;

/// <summary>
/// Copy and row layout for the Why screen. The reason maths stay in <see cref="WhyReasons"/>.
/// Amounts stay in their own fields so a sentence never carries a rand figure.
/// </summary>
public static class WhyScreen
{
    public const int VisibleChips = 3;

    public static string Headline(
        string label,
        decimal current,
        decimal previous,
        MonthPeriod currentPeriod,
        MonthPeriod previousPeriod)
    {
        var now = RandAmounts.MonthName(currentPeriod.Month);
        var then = RandAmounts.MonthName(previousPeriod.Month);
        var delta = current - previous;
        if (delta == 0)
        {
            return $"{label} unchanged in {now} vs {then}";
        }

        var direction = delta > 0 ? "up" : "down";
        return $"{label} {direction} {RandAmounts.Format(Math.Abs(delta))} in {now} vs {then}";
    }

    public static string Lede(
        string label,
        decimal current,
        decimal previous,
        MonthPeriod currentPeriod,
        MonthPeriod previousPeriod)
    {
        var now = RandAmounts.MonthName(currentPeriod.Month);
        var then = RandAmounts.MonthName(previousPeriod.Month);
        var noun = label.ToLowerInvariant();
        var verb = string.Equals(label, "Expenses", StringComparison.Ordinal) ? "were" : "was";
        return $"{now} {noun} {verb} {RandAmounts.Format(current)}. {then} was {RandAmounts.Format(previous)}.";
    }

    public static string CountedTo(MonthPeriod currentPeriod)
    {
        var day = DateTime.DaysInMonth(currentPeriod.Year, currentPeriod.Month);
        return $"Counted to {day} {RandAmounts.MonthName(currentPeriod.Month)} {currentPeriod.Year}";
    }

    public static string ReasonHeading(int namedCount) => namedCount switch
    {
        1 => "One reason",
        2 => "Two reasons",
        3 => "Three reasons",
        _ => $"{namedCount} reasons"
    };

    public static string AddsUpCaption(int namedCount, bool hasRemainder)
    {
        var heading = ReasonHeading(namedCount);
        return hasRemainder ? $"{heading} and everything else" : heading;
    }

    public static string MovementHeading(decimal delta, MonthPeriod previousPeriod)
    {
        var month = RandAmounts.MonthName(previousPeriod.Month);
        if (delta > 0)
        {
            return $"More than {month}";
        }

        if (delta < 0)
        {
            return $"Less than {month}";
        }

        return $"Same as {month}";
    }

    public static string PlainLabel(WhyReasonLine reason) => reason.Name;

    public static string? Context(WhyReasonLine reason)
    {
        if (reason.Remainder)
        {
            var count = reason.RowIds.Count;
            return count == 1 ? "1 small change" : $"{count} small changes";
        }

        if (reason.Amount > 0)
        {
            return "Up";
        }

        if (reason.Amount < 0)
        {
            return "Down";
        }

        return null;
    }

    public static WhyRowSheet Rows(
        HealthMetricKind metric,
        WhyReasonLine reason,
        IReadOnlyList<WhyCitedRow> cited,
        MonthPeriod currentPeriod,
        MonthPeriod previousPeriod)
    {
        var matched = cited
            .Where(row => reason.RowIds.Contains(row.RowId, StringComparer.Ordinal))
            .ToList();
        var currentRows = Order(matched.Where(row => currentPeriod.Contains(row.Date)));
        var previousRows = Order(matched.Where(row => previousPeriod.Contains(row.Date)));
        var currentTotal = currentRows.Sum(row => Signed(metric, row));
        var previousTotal = previousRows.Sum(row => Signed(metric, row));
        var difference = currentTotal - previousTotal;
        var chips = new List<WhySheetChip>();
        var index = 0;
        foreach (var row in currentRows.Concat(previousRows))
        {
            chips.Add(new WhySheetChip(ChipText(row), index >= VisibleChips));
            index++;
        }

        var currentName = RandAmounts.MonthName(currentPeriod.Month);
        var previousName = RandAmounts.MonthName(previousPeriod.Month);
        return new WhyRowSheet
        {
            Caption = Caption(chips.Count, currentName, previousName, currentTotal, previousTotal),
            Current = Band(currentName, currentRows, metric),
            Previous = Band(previousName, previousRows, metric),
            DifferenceLabel = MovementHeading(difference, previousPeriod),
            Difference = difference,
            Chips = chips,
            MoreCount = Math.Max(0, chips.Count - VisibleChips)
        };
    }

    public static WhyHeldSheet Held(
        HealthMetricKind metric,
        string answer,
        decimal cardCurrent,
        decimal cardPrevious,
        IReadOnlyList<WhyCitedRow> cited,
        MonthPeriod currentPeriod,
        MonthPeriod previousPeriod)
    {
        var books = cardCurrent - cardPrevious;
        var noun = Label(metric).ToLowerInvariant();
        if (answer == WhyMessages.HeldUnexplained)
        {
            var spread = WhyReasons.UnexplainedSpread(
                metric,
                cited.Where(row => currentPeriod.Contains(row.Date)).Select(ToTransaction).ToList(),
                cited.Where(row => previousPeriod.Contains(row.Date)).Select(ToTransaction).ToList(),
                books);
            var remainder = spread?.Remainder ?? 0;
            var smallest = spread?.Smallest ?? 0;
            var title = spread is null
                ? "Held. Too much of the change is spread over small rows to name a reason."
                : "Held. Too much of the change is spread over small rows to name a reason. Everything else would be "
                    + RandAmounts.Format(Math.Abs(remainder))
                    + ", bigger than the smallest reason.";
            return new WhyHeldSheet
            {
                Title = title,
                BooksLabel = Went(noun, books),
                BooksAmount = RandAmounts.Format(Math.Abs(books)),
                RowsLabel = remainder < 0 ? "Everything else would be down" : "Everything else would be",
                RowsAmount = RandAmounts.Format(Math.Abs(remainder)),
                GapLabel = "Smallest reason",
                GapAmount = RandAmounts.Format(smallest),
                Checks =
                [
                    "Naming three reasons would leave a remainder bigger than the smallest one.",
                    "Vhona will not guess a reason from that."
                ],
                Foot = "When the rows group more clearly, the reasons show here. Vhona doesn't guess in the meantime."
            };
        }

        var seen = RowsSeen(metric, cited, currentPeriod, previousPeriod);
        return new WhyHeldSheet
        {
            Title = "Held. The rows don't add up to the figure yet, so Vhona won't give a reason.",
            BooksLabel = Went(noun, books),
            BooksAmount = RandAmounts.Format(Math.Abs(books)),
            RowsLabel = "The rows Vhona can see add up to",
            RowsAmount = RandAmounts.Format(seen),
            GapLabel = "With no rows behind it",
            GapAmount = RandAmounts.Format(Math.Abs(books - seen)),
            Checks =
            [
                "The figure on the card and the rows Vhona can add up do not match.",
                "Upload the file with the missing lines, or bring the books in again."
            ],
            Foot = "When the rows add up, the reasons show here. Vhona doesn't guess in the meantime."
        };
    }

    public static decimal RowsSeen(
        HealthMetricKind metric,
        IReadOnlyList<WhyCitedRow> cited,
        MonthPeriod currentPeriod,
        MonthPeriod previousPeriod)
    {
        if (metric == HealthMetricKind.Cash)
        {
            return cited.Where(row => currentPeriod.Contains(row.Date)).Sum(row => row.Amount);
        }

        var currentSum = cited.Where(row => currentPeriod.Contains(row.Date)).Sum(row => Signed(metric, row));
        var previousSum = cited.Where(row => previousPeriod.Contains(row.Date)).Sum(row => Signed(metric, row));
        return currentSum - previousSum;
    }

    private static string Went(string noun, decimal books)
    {
        if (books > 0)
        {
            return $"Your books say {noun} went up";
        }

        if (books < 0)
        {
            return $"Your books say {noun} went down";
        }

        return $"Your books say {noun} is unchanged";
    }

    private static string Caption(
        int rowCount,
        string currentName,
        string previousName,
        decimal currentTotal,
        decimal previousTotal)
    {
        var rows = rowCount == 1 ? "1 row" : $"{rowCount} rows";
        if (previousTotal == 0 && currentTotal != 0)
        {
            return $"{rows}. Nothing like it in {previousName}.";
        }

        return $"{rows}, grouped {currentName} then {previousName}.";
    }

    private static WhyMonthBand Band(string name, IReadOnlyList<WhyCitedRow> rows, HealthMetricKind metric) =>
        new()
        {
            Name = name,
            Total = rows.Sum(row => Signed(metric, row)),
            Lines = rows.Select(row => new WhySheetLine(
                RandAmounts.DayShortMonth(row.Date),
                string.IsNullOrWhiteSpace(row.Description) ? row.Counterparty ?? "" : row.Description,
                Signed(metric, row))).ToList()
        };

    private static List<WhyCitedRow> Order(IEnumerable<WhyCitedRow> rows) =>
        rows.OrderBy(row => row.Date).ThenBy(row => row.SourceRowNumber).ThenBy(row => row.RowId, StringComparer.Ordinal).ToList();

    private static decimal Signed(HealthMetricKind metric, WhyCitedRow row) =>
        metric == HealthMetricKind.Expenses ? Math.Abs(row.Amount) : row.Amount;

    private static string ChipText(WhyCitedRow row) =>
        row.SourceRowNumber > 0 ? "#" + row.SourceRowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) : row.RowId;

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
}

public sealed class WhyRowSheet
{
    public required string Caption { get; init; }
    public required WhyMonthBand Current { get; init; }
    public required WhyMonthBand Previous { get; init; }
    public required string DifferenceLabel { get; init; }
    public required decimal Difference { get; init; }
    public required IReadOnlyList<WhySheetChip> Chips { get; init; }
    public required int MoreCount { get; init; }
}

public sealed class WhyMonthBand
{
    public required string Name { get; init; }
    public required decimal Total { get; init; }
    public required IReadOnlyList<WhySheetLine> Lines { get; init; }
}

public sealed record WhySheetLine(string DateLabel, string Description, decimal Amount);

public sealed record WhySheetChip(string Text, bool Extra);

public sealed class WhyHeldSheet
{
    public required string Title { get; init; }
    public required string BooksLabel { get; init; }
    public required string BooksAmount { get; init; }
    public required string RowsLabel { get; init; }
    public required string RowsAmount { get; init; }
    public required string GapLabel { get; init; }
    public required string GapAmount { get; init; }
    public required IReadOnlyList<string> Checks { get; init; }
    public required string Foot { get; init; }
}
