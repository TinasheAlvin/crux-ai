namespace VhonaAI.Core.Analytics;

/// <summary>
/// Append-only sink for partner events. Local demo uses a JSONL file or in-memory list — no paid vendor.
/// </summary>
public interface IEventLog
{
    void Append(AnalyticsEvent evt);
    IReadOnlyList<AnalyticsEvent> Read();
}
