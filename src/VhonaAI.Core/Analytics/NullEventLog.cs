namespace VhonaAI.Core.Analytics;

public sealed class NullEventLog : IEventLog
{
    public static NullEventLog Instance { get; } = new();

    public void Append(AnalyticsEvent evt)
    {
    }

    public IReadOnlyList<AnalyticsEvent> Read() => [];
}
