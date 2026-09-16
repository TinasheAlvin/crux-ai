using CruxAI.Core.Health;

namespace CruxAI.Core.Why;

public static class WhyMessages
{
    public const string Unverified = "Can't verify that yet.";
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

public sealed class WhyVerification
{
    public required bool Verified { get; init; }
    public required string Question { get; init; }
    public required string Answer { get; init; }
    public HealthMetricKind? Metric { get; init; }
    public required IReadOnlyList<WhyCitationDraft> Citations { get; init; }

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

    public IReadOnlyList<string> CitedRowIds => CitedRows.Select(row => row.RowId).ToList();
}
