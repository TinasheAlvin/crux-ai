namespace CruxAI.Core.Analytics;

/// <summary>
/// Circuit-scoped flags so session-one and next-visit signals do not double-fire in one Blazor circuit.
/// </summary>
public sealed class PartnerSession
{
    private int _whyAsks;

    public bool OptedInThisSession { get; set; }
    public bool BriefReturnTracked { get; set; }
    public HashSet<Guid> OpenedReceipts { get; } = [];
    public HashSet<Guid> RatedAnswers { get; } = [];
    public bool WaitlistSignaled { get; set; }

    public bool TryMarkFirstWhyAsk() => Interlocked.Increment(ref _whyAsks) == 1;
}
