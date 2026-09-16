namespace CruxAI.Core.Analytics;

public sealed class AnalyticsEvent
{
    public required string Name { get; init; }
    public Guid OrgId { get; init; }
    public Guid UserId { get; init; }
    public DateTime Timestamp { get; init; }
    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>();
}
