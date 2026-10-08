using VhonaAI.Core.Entities;

namespace VhonaAI.Core.Calling;

public readonly record struct WhoToCallThresholds(
    int StoppedMissedCycles,
    decimal DroppedPercent,
    int DroppedMonths,
    int MinimumInvoiceHistory)
{
    public static WhoToCallThresholds FromDefaults() => new(
        CallThresholdDefaults.StoppedMissedCycles,
        CallThresholdDefaults.DroppedPercent,
        CallThresholdDefaults.DroppedMonths,
        CallThresholdDefaults.MinimumInvoiceHistory);
}

public enum CallFlagKind
{
    Late = 0,
    Stopped = 1,
    Dropped = 2
}

public enum CallActionKind
{
    Called = 0,
    Snoozed = 1,
    Paid = 2,
    NotAConcern = 3
}

/// <summary>One business's invoices, already limited to that business. The rules do not query a database.</summary>
public sealed class WhoToCallBooks
{
    public DateOnly AsAt { get; init; }
    public string BusinessName { get; init; } = string.Empty;
    public IReadOnlyList<WhoToCallCustomerBook> Customers { get; init; } = [];
}

public sealed class WhoToCallCustomerBook
{
    public Guid CustomerId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string RowId { get; init; } = string.Empty;
    public string? ContactPerson { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public bool IsActive { get; init; } = true;
    public IReadOnlyList<WhoToCallInvoice> Invoices { get; init; } = [];
}

public sealed class WhoToCallInvoice
{
    public string RowId { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public DateOnly InvoiceDate { get; init; }
    public DateOnly? DueDate { get; init; }
    public decimal Amount { get; init; }
    public decimal AmountDue { get; init; }
    public InvoiceStatus Status { get; init; }
    public DateOnly? PaidDate { get; init; }
    public string? Terms { get; init; }
    public DateTime ImportedAt { get; init; }
    public IReadOnlyList<WhoToCallLine> Lines { get; init; } = [];
}

public sealed class WhoToCallLine
{
    public string RowId { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}

public sealed class WhoToCallResult
{
    public const string SortExplanation =
        "Sorted by Late (most days overdue first), then Stopped (longest silence first), then Dropped. This is not a ranking and there is no score.";

    public const string ReceiptFooter = "Flagged from these rows only. No score.";

    public DateOnly AsAt { get; init; }
    public string BusinessName { get; init; } = string.Empty;
    public IReadOnlyList<WhoToCallFlag> Flags { get; init; } = [];
}

public sealed class WhoToCallFlag
{
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerRowId { get; init; } = string.Empty;
    public string? ContactPerson { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public CallFlagKind Kind { get; init; }
    public string Why { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public IReadOnlyList<string> CitedRowIds { get; init; } = [];
    public int RowCount => CitedRowIds.Count;
    public int? OldestDaysOverdue { get; init; }
    public int OpenInvoiceCount { get; init; }
    public decimal OpenTotal { get; init; }
    public int DaysSinceLastInvoice { get; init; }
    public string EvidenceKey { get; init; } = string.Empty;
    public string? DraftBody { get; set; }
    public LateReceipt? Late { get; init; }
    public StoppedReceipt? Stopped { get; init; }
    public DroppedReceipt? Dropped { get; init; }
}

public sealed class LateInvoiceRow
{
    public string RowId { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public DateOnly Issued { get; init; }
    public DateOnly? Due { get; init; }
    public decimal Amount { get; init; }
    public decimal AmountDue { get; init; }
    public bool IsOpen { get; init; }
    public bool NotDueYet { get; init; }
    public int? DaysOverdue { get; init; }
    public DateOnly? PaidDate { get; init; }
}

public sealed class LateReceipt
{
    public string? Terms { get; init; }
    public string? ContactPerson { get; init; }
    public string Intro { get; init; } = string.Empty;
    public IReadOnlyList<LateInvoiceRow> Invoices { get; init; } = [];
    public IReadOnlyList<LateInvoiceRow> OpenInvoices { get; init; } = [];
}

public sealed class StoppedInvoiceRow
{
    public string RowId { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public DateOnly InvoiceDate { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}

public sealed class StoppedReceipt
{
    public string Cadence { get; init; } = string.Empty;
    public int MissedCycles { get; init; }
    public DateOnly LastInvoiceDate { get; init; }
    public IReadOnlyList<StoppedInvoiceRow> Invoices { get; init; } = [];
}

public sealed class DroppedLineRow
{
    public string Description { get; init; } = string.Empty;
    public bool Vanished { get; init; }
    public IReadOnlyList<decimal?> MonthAmounts { get; init; } = [];
}

public sealed class DroppedReceipt
{
    public IReadOnlyList<DateOnly> Months { get; init; } = [];
    public IReadOnlyList<DroppedLineRow> Lines { get; init; } = [];
    public IReadOnlyList<decimal> Totals { get; init; } = [];
    public string? VanishedDescription { get; init; }
}

public sealed record WhoToCallBriefLine(int CustomerCount, string Text);
