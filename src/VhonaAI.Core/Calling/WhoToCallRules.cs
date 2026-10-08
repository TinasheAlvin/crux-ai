using VhonaAI.Core.Entities;
using VhonaAI.Core.Parsing;

namespace VhonaAI.Core.Calling;

/// <summary>
/// Fixed who-to-call rules. No model and no score. A flag is returned only with the rows that prove it.
/// </summary>
public static class WhoToCallRules
{
    public static WhoToCallResult Evaluate(WhoToCallBooks books, WhoToCallThresholds thresholds)
    {
        var flags = new List<WhoToCallFlag>();
        foreach (var customer in books.Customers.Where(item => item.IsActive))
        {
            var flag = EvaluateCustomer(customer, books.AsAt, books.BusinessName, thresholds);
            if (flag is { CitedRowIds.Count: > 0 })
            {
                flags.Add(flag);
            }
        }

        var ordered = flags
            .OrderBy(item => KindRank(item.Kind))
            .ThenByDescending(item => item.Kind == CallFlagKind.Late ? item.OldestDaysOverdue ?? 0 : 0)
            .ThenByDescending(item => item.Kind == CallFlagKind.Stopped ? item.DaysSinceLastInvoice : 0)
            .ThenByDescending(item => item.Kind == CallFlagKind.Dropped ? DropSize(item) : 0)
            .ThenBy(item => item.CustomerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WhoToCallResult
        {
            AsAt = books.AsAt,
            BusinessName = books.BusinessName,
            Flags = ordered
        };
    }

    private static int KindRank(CallFlagKind kind) => kind switch
    {
        CallFlagKind.Late => 0,
        CallFlagKind.Stopped => 1,
        CallFlagKind.Dropped => 2,
        _ => 3
    };

    private static decimal DropSize(WhoToCallFlag flag)
    {
        if (flag.Dropped is null || flag.Dropped.Totals.Count == 0)
        {
            return 0;
        }

        var recent = flag.Dropped.Totals.TakeLast(1).FirstOrDefault();
        var earlier = flag.Dropped.Totals.FirstOrDefault();
        return earlier <= 0 ? 0 : (earlier - recent) / earlier;
    }

    private static WhoToCallFlag? EvaluateCustomer(
        WhoToCallCustomerBook customer,
        DateOnly asAt,
        string businessName,
        WhoToCallThresholds thresholds)
    {
        var countable = customer.Invoices
            .Where(invoice => IsCountable(invoice) && invoice.InvoiceDate <= asAt)
            .OrderBy(invoice => invoice.InvoiceDate)
            .ThenBy(invoice => invoice.Number, StringComparer.Ordinal)
            .ToList();

        var late = TryLate(customer, countable, asAt, businessName);
        if (late is not null)
        {
            return late;
        }

        var stopped = TryStopped(customer, countable, asAt, thresholds);
        if (stopped is not null)
        {
            return stopped;
        }

        return TryDropped(customer, countable, asAt, thresholds);
    }

    /// <summary>Open and Overdue are the same unpaid invoice. The file's word does not change the rule.</summary>
    public static bool IsUnpaid(InvoiceStatus status) =>
        status is InvoiceStatus.Open or InvoiceStatus.Overdue;

    public static bool IsCountableStatus(InvoiceStatus status) =>
        IsUnpaid(status) || status == InvoiceStatus.Paid;

    private static bool IsCountable(WhoToCallInvoice invoice) =>
        IsCountableStatus(invoice.Status);

    private static WhoToCallFlag? TryLate(
        WhoToCallCustomerBook customer,
        IReadOnlyList<WhoToCallInvoice> countable,
        DateOnly asAt,
        string businessName)
    {
        var open = countable
            .Where(invoice => IsUnpaid(invoice.Status)
                              && invoice.AmountDue > 0
                              && invoice.DueDate is DateOnly due
                              && due < asAt)
            .OrderBy(invoice => invoice.DueDate)
            .ThenBy(invoice => invoice.Number, StringComparer.Ordinal)
            .ToList();
        if (open.Count == 0)
        {
            return null;
        }

        if (countable.Any(invoice => string.IsNullOrWhiteSpace(invoice.RowId)))
        {
            return null;
        }

        var rows = countable.Select(invoice => ToLateRow(invoice, asAt)).ToList();
        var openRows = rows.Where(row => row.IsOpen).OrderBy(row => row.Due).ToList();
        var cited = countable.Select(invoice => invoice.RowId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (cited.Count == 0 || openRows.Count == 0)
        {
            return null;
        }

        var oldest = openRows.Max(row => row.DaysOverdue ?? 0);
        var total = openRows.Sum(row => row.AmountDue);
        var oldestInvoice = openRows.OrderByDescending(row => row.DaysOverdue).First();
        var paidOnTime = countable
            .Where(invoice => invoice.Status == InvoiceStatus.Paid && invoice.PaidDate is not null && invoice.DueDate is not null)
            .All(invoice => invoice.PaidDate <= invoice.DueDate);
        var intro = open.Count == 1
            ? $"One invoice, {RandAmounts.Format(total)} in total, is past its due date."
            : $"{open.Count} invoices, {RandAmounts.Format(total)} in total, are past their due dates.";
        if (paidOnTime && countable.Any(invoice => invoice.Status == InvoiceStatus.Paid))
        {
            intro = "The earlier invoices were paid on or before their due dates. Now " + char.ToLowerInvariant(intro[0]) + intro[1..];
        }

        var flag = new WhoToCallFlag
        {
            CustomerId = customer.CustomerId,
            CustomerName = customer.Name,
            CustomerRowId = customer.RowId,
            ContactPerson = customer.ContactPerson,
            Phone = customer.Phone,
            Email = customer.Email,
            Kind = CallFlagKind.Late,
            Why = open.Count switch
            {
                1 => $"One invoice open, {oldest} days overdue",
                2 => $"Two invoices open, oldest {oldest} days overdue",
                _ => $"{open.Count} invoices open, oldest {oldest} days overdue"
            },
            Detail = $"Oldest open {oldestInvoice.Number}, due {(oldestInvoice.Due is DateOnly due ? RandAmounts.DayMonth(due) : "an unknown date")}.",
            CitedRowIds = cited,
            OldestDaysOverdue = oldest,
            OpenInvoiceCount = openRows.Count,
            OpenTotal = total,
            DaysSinceLastInvoice = DaysSince(countable, asAt),
            EvidenceKey = Evidence(CallFlagKind.Late, cited),
            Late = new LateReceipt
            {
                Terms = open.Select(invoice => invoice.Terms).FirstOrDefault(terms => !string.IsNullOrWhiteSpace(terms))
                    ?? countable.Select(invoice => invoice.Terms).FirstOrDefault(terms => !string.IsNullOrWhiteSpace(terms)),
                ContactPerson = customer.ContactPerson,
                Intro = intro,
                Invoices = rows,
                OpenInvoices = openRows
            }
        };

        return new WhoToCallFlag
        {
            CustomerId = flag.CustomerId,
            CustomerName = flag.CustomerName,
            CustomerRowId = flag.CustomerRowId,
            ContactPerson = flag.ContactPerson,
            Phone = flag.Phone,
            Email = flag.Email,
            Kind = flag.Kind,
            Why = flag.Why,
            Detail = flag.Detail,
            CitedRowIds = flag.CitedRowIds,
            OldestDaysOverdue = flag.OldestDaysOverdue,
            OpenInvoiceCount = flag.OpenInvoiceCount,
            OpenTotal = flag.OpenTotal,
            DaysSinceLastInvoice = flag.DaysSinceLastInvoice,
            EvidenceKey = flag.EvidenceKey,
            Late = flag.Late,
            DraftBody = ReminderDraftTemplate.Build(flag, businessName)
        };
    }

    private static LateInvoiceRow ToLateRow(WhoToCallInvoice invoice, DateOnly asAt)
    {
        var unpaid = IsUnpaid(invoice.Status) && invoice.AmountDue > 0;
        var overdue = unpaid
                      && invoice.DueDate is DateOnly due
                      && due < asAt
            ? asAt.DayNumber - due.DayNumber
            : (int?)null;
        return new LateInvoiceRow
        {
            RowId = invoice.RowId,
            Number = invoice.Number,
            Issued = invoice.InvoiceDate,
            Due = invoice.DueDate,
            Amount = invoice.Amount,
            AmountDue = IsUnpaid(invoice.Status) ? invoice.AmountDue : invoice.Amount,
            IsOpen = overdue is not null,
            NotDueYet = unpaid && overdue is null,
            DaysOverdue = overdue,
            PaidDate = invoice.PaidDate
        };
    }

    private static WhoToCallFlag? TryStopped(
        WhoToCallCustomerBook customer,
        IReadOnlyList<WhoToCallInvoice> countable,
        DateOnly asAt,
        WhoToCallThresholds thresholds)
    {
        if (countable.Count < thresholds.MinimumInvoiceHistory)
        {
            return null;
        }

        var cycle = MedianGap(countable.Select(invoice => invoice.InvoiceDate).ToList());
        if (cycle is null or < 1)
        {
            return null;
        }

        var last = countable[^1];
        var days = asAt.DayNumber - last.InvoiceDate.DayNumber;
        var missed = days / cycle.Value;
        if (missed < thresholds.StoppedMissedCycles)
        {
            return null;
        }

        if (countable.Any(invoice => string.IsNullOrWhiteSpace(invoice.RowId)))
        {
            return null;
        }

        var cited = countable.Select(invoice => invoice.RowId).Distinct().ToList();
        var cadence = CadenceLabel(cycle.Value);
        var description = PrimaryDescription(last);
        return new WhoToCallFlag
        {
            CustomerId = customer.CustomerId,
            CustomerName = customer.Name,
            CustomerRowId = customer.RowId,
            ContactPerson = customer.ContactPerson,
            Phone = customer.Phone,
            Email = customer.Email,
            Kind = CallFlagKind.Stopped,
            Why = $"{cadence} invoices stopped after {RandAmounts.DayMonth(last.InvoiceDate)}",
            Detail = $"Last invoice {RandAmounts.DayMonth(last.InvoiceDate)}. {description}, {RandAmounts.Format(last.Amount)}, not billed since.",
            CitedRowIds = cited,
            DaysSinceLastInvoice = days,
            EvidenceKey = Evidence(CallFlagKind.Stopped, cited),
            Stopped = new StoppedReceipt
            {
                Cadence = cadence,
                MissedCycles = missed,
                LastInvoiceDate = last.InvoiceDate,
                Invoices = countable.Select(invoice => new StoppedInvoiceRow
                {
                    RowId = invoice.RowId,
                    Number = invoice.Number,
                    InvoiceDate = invoice.InvoiceDate,
                    Description = PrimaryDescription(invoice),
                    Amount = invoice.Amount
                }).ToList()
            }
        };
    }

    private static WhoToCallFlag? TryDropped(
        WhoToCallCustomerBook customer,
        IReadOnlyList<WhoToCallInvoice> countable,
        DateOnly asAt,
        WhoToCallThresholds thresholds)
    {
        if (countable.Count < thresholds.MinimumInvoiceHistory || thresholds.DroppedMonths < 1)
        {
            return null;
        }

        var months = MonthTotals(countable);
        var windowEnd = new DateOnly(asAt.Year, asAt.Month, 1);
        var recent = Enumerable.Range(0, thresholds.DroppedMonths)
            .Select(offset => windowEnd.AddMonths(-(thresholds.DroppedMonths - 1 - offset)))
            .ToList();
        var baselineKeys = months.Keys.Where(month => month < recent[0]).OrderBy(month => month).ToList();
        if (baselineKeys.Count == 0)
        {
            return null;
        }

        var baselineAmounts = baselineKeys.Select(month => months[month].Total).OrderBy(amount => amount).ToList();
        var baseline = Median(baselineAmounts);
        if (baseline <= 0)
        {
            return null;
        }

        var recentTotals = recent.Select(month => months.TryGetValue(month, out var bucket) ? bucket.Total : 0m).ToList();
        // A month with no invoices is silence. That belongs to the stopped rule, not a drop.
        var stillBilling = recentTotals.Any(total => total > 0);
        var percentDrop = stillBilling
            && recentTotals.All(total => (baseline - total) / baseline * 100m >= thresholds.DroppedPercent);

        var vanished = FindVanishedLine(months, baselineKeys, recent);
        if (!percentDrop && vanished is null)
        {
            return null;
        }

        var shown = baselineKeys.Concat(recent).ToList();
        var citedInvoices = shown
            .Where(month => months.ContainsKey(month))
            .SelectMany(month => months[month].Invoices)
            .ToList();
        if (citedInvoices.Count == 0 || citedInvoices.Any(invoice => string.IsNullOrWhiteSpace(invoice.RowId)))
        {
            return null;
        }

        var cited = citedInvoices.Select(invoice => invoice.RowId).Distinct().ToList();

        var recentTypical = Median(recentTotals.OrderBy(amount => amount).ToList());
        var since = RandAmounts.MonthName(recent[0].Month);
        var why = $"Dropped from {RandAmounts.About(baseline)} to {RandAmounts.About(recentTypical)} a month since {since}";
        var detail = vanished is null
            ? $"Monthly billing is at least {thresholds.DroppedPercent:0}% below the earlier months for {thresholds.DroppedMonths} months."
            : VanishedDetail(vanished, months, recent);

        var descriptions = shown
            .SelectMany(month => LineKeys(month, months))
            .Distinct(StringComparer.Ordinal)
            .Select(key => months.SelectMany(pair => pair.Value.Lines.Where(line => line.Key == key).Select(line => line.Value.Label)).First())
            .ToList();
        if (vanished is not null)
        {
            descriptions = descriptions
                .OrderByDescending(label => CustomerNames.Normalize(label) == vanished.Key)
                .ThenBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var lineRows = descriptions.Select(label =>
        {
            var key = CustomerNames.Normalize(label);
            return new DroppedLineRow
            {
                Description = label,
                Vanished = vanished is not null && key == vanished.Key,
                MonthAmounts = shown.Select(month =>
                    months.TryGetValue(month, out var bucket) && bucket.Lines.TryGetValue(key, out var line)
                        ? line.Amount
                        : (decimal?)null).ToList()
            };
        }).ToList();

        return new WhoToCallFlag
        {
            CustomerId = customer.CustomerId,
            CustomerName = customer.Name,
            CustomerRowId = customer.RowId,
            ContactPerson = customer.ContactPerson,
            Phone = customer.Phone,
            Email = customer.Email,
            Kind = CallFlagKind.Dropped,
            Why = why,
            Detail = detail,
            CitedRowIds = cited,
            DaysSinceLastInvoice = DaysSince(countable, asAt),
            EvidenceKey = Evidence(CallFlagKind.Dropped, cited),
            Dropped = new DroppedReceipt
            {
                Months = shown,
                Lines = lineRows,
                Totals = shown.Select(month => months.TryGetValue(month, out var bucket) ? bucket.Total : 0m).ToList(),
                VanishedDescription = vanished?.Label
            }
        };
    }

    private static string VanishedDetail(
        LineIdentity vanished,
        IReadOnlyDictionary<DateOnly, MonthBucket> months,
        IReadOnlyList<DateOnly> recent)
    {
        var continuing = recent
            .SelectMany(month => LineValues(months, month))
            .GroupBy(line => line.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .Where(line => line.Key != vanished.Key)
            .OrderByDescending(line => line.Amount)
            .FirstOrDefault();
        var since = RandAmounts.MonthName(recent[0].Month);
        var text = $"{vanished.Label}, {RandAmounts.About(vanished.Amount)} a month, dropped from {since} invoices.";
        if (continuing is not null)
        {
            text += $" {continuing.Label}, {RandAmounts.Format(continuing.Amount)}, still billed.";
        }

        return text;
    }

    private static LineIdentity? FindVanishedLine(
        IReadOnlyDictionary<DateOnly, MonthBucket> months,
        IReadOnlyList<DateOnly> baseline,
        IReadOnlyList<DateOnly> recent)
    {
        var counts = new Dictionary<string, (int Count, decimal Amount, string Label)>(StringComparer.Ordinal);
        foreach (var month in baseline)
        {
            if (!months.TryGetValue(month, out var bucket))
            {
                continue;
            }

            foreach (var line in bucket.Lines.Values)
            {
                if (counts.TryGetValue(line.Key, out var existing))
                {
                    counts[line.Key] = (existing.Count + 1, line.Amount, line.Label);
                }
                else
                {
                    counts[line.Key] = (1, line.Amount, line.Label);
                }
            }
        }

        LineIdentity? found = null;
        foreach (var (key, value) in counts)
        {
            if (value.Count < 2)
            {
                continue;
            }

            var missing = recent.All(month =>
                !months.TryGetValue(month, out var bucket) || !bucket.Lines.ContainsKey(key));
            if (!missing)
            {
                continue;
            }

            var othersContinue = recent.Any(month =>
                months.TryGetValue(month, out var bucket) && bucket.Lines.Keys.Any(other => other != key));
            if (!othersContinue)
            {
                continue;
            }

            var amounts = baseline
                .Where(month => months.TryGetValue(month, out var bucket) && bucket.Lines.ContainsKey(key))
                .Select(month => months[month].Lines[key].Amount)
                .OrderBy(amount => amount)
                .ToList();
            found = new LineIdentity(key, value.Label, Median(amounts));
            break;
        }

        return found;
    }

    private static Dictionary<DateOnly, MonthBucket> MonthTotals(IReadOnlyList<WhoToCallInvoice> invoices)
    {
        var months = new Dictionary<DateOnly, MonthBucket>();
        foreach (var invoice in invoices)
        {
            var key = new DateOnly(invoice.InvoiceDate.Year, invoice.InvoiceDate.Month, 1);
            if (!months.TryGetValue(key, out var bucket))
            {
                bucket = new MonthBucket();
                months[key] = bucket;
            }

            bucket.Total += invoice.Amount;
            bucket.Invoices.Add(invoice);
            var lines = invoice.Lines.Count > 0
                ? invoice.Lines.Select(line => (line.Description, line.Amount))
                : [(invoice.Number, invoice.Amount)];
            foreach (var (description, amount) in lines)
            {
                if (string.IsNullOrWhiteSpace(description))
                {
                    continue;
                }

                var lineKey = CustomerNames.Normalize(description);
                if (bucket.Lines.TryGetValue(lineKey, out var line))
                {
                    bucket.Lines[lineKey] = line with { Amount = line.Amount + amount };
                }
                else
                {
                    bucket.Lines[lineKey] = new LineIdentity(lineKey, description.Trim(), amount);
                }
            }
        }

        return months;
    }

    private static int? MedianGap(IReadOnlyList<DateOnly> dates)
    {
        if (dates.Count < 2)
        {
            return null;
        }

        var ordered = dates.Distinct().OrderBy(date => date).ToList();
        var gaps = new List<int>();
        for (var i = 1; i < ordered.Count; i++)
        {
            var gap = ordered[i].DayNumber - ordered[i - 1].DayNumber;
            if (gap > 0)
            {
                gaps.Add(gap);
            }
        }

        if (gaps.Count == 0)
        {
            return null;
        }

        gaps.Sort();
        return gaps[gaps.Count / 2];
    }

    private static decimal Median(IReadOnlyList<decimal> ordered)
    {
        if (ordered.Count == 0)
        {
            return 0;
        }

        return ordered[ordered.Count / 2];
    }

    private static IEnumerable<string> LineKeys(DateOnly month, IReadOnlyDictionary<DateOnly, MonthBucket> months) =>
        months.TryGetValue(month, out var bucket) ? bucket.Lines.Keys : [];

    private static IEnumerable<LineIdentity> LineValues(IReadOnlyDictionary<DateOnly, MonthBucket> months, DateOnly month) =>
        months.TryGetValue(month, out var bucket) ? bucket.Lines.Values : [];

    private static string CadenceLabel(int cycleDays)
    {
        if (cycleDays <= 10)
        {
            return "Weekly";
        }

        if (cycleDays <= 20)
        {
            return "Every few weeks";
        }

        if (cycleDays <= 40)
        {
            return "Monthly";
        }

        return $"About every {cycleDays} days";
    }

    private static string PrimaryDescription(WhoToCallInvoice invoice)
    {
        var line = invoice.Lines.OrderByDescending(item => item.Amount).FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Description));
        return line is null ? invoice.Number : line.Description.Trim();
    }

    private static int DaysSince(IReadOnlyList<WhoToCallInvoice> invoices, DateOnly asAt)
    {
        if (invoices.Count == 0)
        {
            return 0;
        }

        return Math.Max(0, asAt.DayNumber - invoices[^1].InvoiceDate.DayNumber);
    }

    private static string Evidence(CallFlagKind kind, IReadOnlyList<string> rowIds) =>
        kind + "|" + string.Join(",", rowIds.OrderBy(id => id, StringComparer.Ordinal));

    private sealed class MonthBucket
    {
        public decimal Total { get; set; }
        public List<WhoToCallInvoice> Invoices { get; } = [];
        public Dictionary<string, LineIdentity> Lines { get; } = new(StringComparer.Ordinal);
    }

    private sealed record LineIdentity(string Key, string Label, decimal Amount);
}
