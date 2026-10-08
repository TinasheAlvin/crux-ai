using System.Text;
using VhonaAI.Core.Calling;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Health;

namespace VhonaAI.Core.Why;

public static class WhyVerifier
{
    public static WhyVerification Verify(
        string question,
        IReadOnlyList<Transaction> transactions,
        HealthSnapshot snapshot,
        HealthMetricKind? seededMetric = null)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return WhyVerification.FailClosed(question ?? string.Empty);
        }

        var intent = WhyIntentParser.Parse(question, seededMetric);
        return Verify(question.Trim(), intent, transactions, snapshot);
    }

    public static WhyVerification Verify(
        string question,
        WhyIntent intent,
        IReadOnlyList<Transaction> transactions,
        HealthSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(question) || intent.Metric is null || !snapshot.HasData)
        {
            return WhyVerification.FailClosed(question ?? string.Empty);
        }

        var metric = intent.Metric.Value;
        var card = snapshot.Card(metric);
        if (card is null || snapshot.CurrentPeriod is null || snapshot.PreviousPeriod is null)
        {
            return WhyVerification.FailClosed(question);
        }

        var byId = transactions
            .Where(t => !string.IsNullOrWhiteSpace(t.RowId))
            .GroupBy(t => t.RowId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var currentRows = Resolve(card.CurrentRowIds, byId);
        var previousRows = Resolve(card.PreviousRowIds, byId);

        if (intent.WantsChange)
        {
            if (currentRows.Count == 0 || previousRows.Count == 0)
            {
                return WhyVerification.FailClosed(question);
            }

            var currentValue = ValueFromRows(metric, currentRows);
            var previousValue = ValueFromRows(metric, previousRows);
            if (currentValue is null || previousValue is null)
            {
                return WhyVerification.FailClosed(question);
            }

            var columns = ColumnsFor(metric);
            var citations = Cite(currentRows, columns, snapshot.CurrentPeriod.Label)
                .Concat(Cite(previousRows, columns, snapshot.PreviousPeriod.Label))
                .ToList();
            if (citations.Count == 0)
            {
                return WhyVerification.FailClosed(question);
            }

            var delta = currentValue.Value - previousValue.Value;
            var answer = BuildChangeAnswer(
                metric,
                snapshot.CurrentPeriod,
                snapshot.PreviousPeriod,
                currentValue.Value,
                previousValue.Value,
                delta,
                currentRows,
                previousRows);

            return new WhyVerification
            {
                Verified = true,
                Question = question,
                Answer = answer,
                Metric = metric,
                Citations = citations
            };
        }

        if (currentRows.Count == 0)
        {
            return WhyVerification.FailClosed(question);
        }

        var levelValue = ValueFromRows(metric, currentRows);
        if (levelValue is null)
        {
            return WhyVerification.FailClosed(question);
        }

        var levelColumns = ColumnsFor(metric);
        var levelCitations = Cite(currentRows, levelColumns, snapshot.CurrentPeriod.Label).ToList();
        if (levelCitations.Count == 0)
        {
            return WhyVerification.FailClosed(question);
        }

        return new WhyVerification
        {
            Verified = true,
            Question = question,
            Answer = BuildLevelAnswer(metric, snapshot.CurrentPeriod, levelValue.Value, currentRows),
            Metric = metric,
            Citations = levelCitations
        };
    }

    public static string ColumnsFor(HealthMetricKind metric) =>
        metric == HealthMetricKind.Cash
            ? "Date, Description, Amount, Balance"
            : "Date, Description, Amount";

    private static IReadOnlyList<Transaction> Resolve(
        IEnumerable<string> rowIds,
        IReadOnlyDictionary<string, Transaction> byId)
    {
        var rows = new List<Transaction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rowId in rowIds)
        {
            if (!seen.Add(rowId))
            {
                continue;
            }

            if (byId.TryGetValue(rowId, out var row))
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    private static IEnumerable<WhyCitationDraft> Cite(
        IEnumerable<Transaction> rows,
        string columns,
        string periodLabel)
    {
        foreach (var row in rows)
        {
            yield return new WhyCitationDraft
            {
                RowId = row.RowId,
                Columns = columns,
                PeriodLabel = periodLabel
            };
        }
    }

    private static decimal? ValueFromRows(HealthMetricKind metric, IReadOnlyList<Transaction> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        return metric switch
        {
            HealthMetricKind.Revenue => rows.Sum(t => t.Amount),
            HealthMetricKind.Expenses => rows.Sum(t => Math.Abs(t.Amount)),
            HealthMetricKind.Profit => rows.Sum(t => t.Amount),
            HealthMetricKind.Cash => LatestBalance(rows),
            _ => null
        };
    }

    private static decimal? LatestBalance(IReadOnlyList<Transaction> rows) =>
        rows.Where(t => t.Balance.HasValue)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.SourceRowNumber)
            .LastOrDefault()
            ?.Balance;

    private static string BuildChangeAnswer(
        HealthMetricKind metric,
        MonthPeriod currentPeriod,
        MonthPeriod previousPeriod,
        decimal current,
        decimal previous,
        decimal delta,
        IReadOnlyList<Transaction> currentRows,
        IReadOnlyList<Transaction> previousRows)
    {
        var name = LabelFor(metric);
        var sb = new StringBuilder();
        sb.Append(name)
            .Append(" changed from ")
            .Append(Money(previous))
            .Append(" in ")
            .Append(previousPeriod.Label)
            .Append(" to ")
            .Append(Money(current))
            .Append(" in ")
            .Append(currentPeriod.Label)
            .Append(" (")
            .Append(DeltaPhrase(delta))
            .Append(").");

        sb.AppendLine();
        sb.AppendLine();
        AppendValueBasis(sb, metric, currentPeriod.Label, current, currentRows);
        AppendValueBasis(sb, metric, previousPeriod.Label, previous, previousRows);
        return sb.ToString();
    }

    private static string BuildLevelAnswer(
        HealthMetricKind metric,
        MonthPeriod currentPeriod,
        decimal current,
        IReadOnlyList<Transaction> currentRows)
    {
        var sb = new StringBuilder();
        sb.Append(LabelFor(metric))
            .Append(" is ")
            .Append(Money(current))
            .Append(" in ")
            .Append(currentPeriod.Label)
            .Append('.');
        sb.AppendLine();
        sb.AppendLine();
        AppendValueBasis(sb, metric, currentPeriod.Label, current, currentRows);
        return sb.ToString();
    }

    private static void AppendValueBasis(
        StringBuilder sb,
        HealthMetricKind metric,
        string periodLabel,
        decimal value,
        IReadOnlyList<Transaction> rows)
    {
        if (metric == HealthMetricKind.Cash)
        {
            sb.Append(periodLabel)
                .Append(" cash is the latest Balance on ")
                .Append(rows.Count)
                .Append(" cited row(s) (")
                .Append(Money(value))
                .AppendLine(").");
            return;
        }

        var basis = metric switch
        {
            HealthMetricKind.Revenue => "sum of Amount on cited inflow rows",
            HealthMetricKind.Expenses => "sum of absolute Amount on cited outflow rows",
            HealthMetricKind.Profit => "sum of Amount on cited period rows",
            _ => "cited rows"
        };

        sb.Append(periodLabel)
            .Append(' ')
            .Append(Money(value))
            .Append(" is the ")
            .Append(basis)
            .Append(" (")
            .Append(rows.Count)
            .AppendLine(").");
    }

    private static string LabelFor(HealthMetricKind metric) => metric switch
    {
        HealthMetricKind.Revenue => "Revenue",
        HealthMetricKind.Expenses => "Expenses",
        HealthMetricKind.Profit => "Profit",
        HealthMetricKind.Cash => "Cash",
        _ => metric.ToString()
    };

    private static string Money(decimal value) => RandAmounts.Format(value);

    private static string DeltaPhrase(decimal delta)
    {
        if (delta > 0)
        {
            return "up " + Money(delta);
        }

        if (delta < 0)
        {
            return "down " + Money(Math.Abs(delta));
        }

        return "unchanged";
    }
}
