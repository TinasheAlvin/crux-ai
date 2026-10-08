using VhonaAI.Core.Health;

namespace VhonaAI.Core.Why;

public static class WhyMessages
{
    public const string Unverified = "Can't verify that yet.";
    public const string TryAnother = "Try another question";
    public const string Checking = "Checking persisted rows…";
    public const string Held = "Held: the rows don't add up to the change, so Vhona won't guess";
    public const string HeldUnexplained = "Held: three reasons would leave too much unexplained, so Vhona won't guess";
    public const string EverythingElse = "Everything else";
}

public sealed class WhyIntent
{
    public HealthMetricKind? Metric { get; init; }
    public bool WantsChange { get; init; }
}

public sealed class WhyCitationDraft
{
    public required string RowId { get; init; }
    public required string Columns { get; init; }
    public string? PeriodLabel { get; init; }
}

public sealed class WhyReasonLine
{
    public required string Title { get; init; }
    public required string Name { get; init; }
    public required decimal Amount { get; init; }
    public required bool Remainder { get; init; }
    public required IReadOnlyList<string> RowIds { get; init; }
}

public sealed class WhyVerification
{
    public required bool Verified { get; init; }
    public bool Held { get; init; }
    public required string Question { get; init; }
    public required string Answer { get; init; }
    public HealthMetricKind? Metric { get; init; }
    public required IReadOnlyList<WhyCitationDraft> Citations { get; init; }
    public IReadOnlyList<WhyReasonLine> Reasons { get; init; } = [];

    public static WhyVerification FailClosed(string question) => new()
    {
        Verified = false,
        Question = question,
        Answer = WhyMessages.Unverified,
        Metric = null,
        Citations = []
    };
}

public sealed class WhyCitedRow
{
    public required string RowId { get; init; }
    public required string Columns { get; init; }
    public string? PeriodLabel { get; init; }
    public DateOnly Date { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? Category { get; init; }
    public string? Counterparty { get; init; }
    public string? Reference { get; init; }
    public decimal? Balance { get; init; }
    public int SourceRowNumber { get; init; }
}

public sealed class WhyAskResult
{
    public required Guid AnswerId { get; init; }
    public required string Question { get; init; }
    public required string Answer { get; init; }
    public required bool Verified { get; init; }
    public HealthMetricKind? Metric { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required IReadOnlyList<WhyCitedRow> CitedRows { get; init; }
    public bool Held { get; init; }
    public IReadOnlyList<WhyReasonLine> Reasons { get; init; } = [];

    public IReadOnlyList<string> CitedRowIds => CitedRows.Select(row => row.RowId).ToList();
}
