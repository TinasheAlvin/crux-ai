using System.Collections.Concurrent;
using VhonaAI.Core.Analytics;

namespace VhonaAI.Infrastructure.Analytics;

public sealed class InMemoryEventLog : IEventLog
{
    private readonly ConcurrentQueue<AnalyticsEvent> _events = new();

    public void Append(AnalyticsEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        _events.Enqueue(evt);
    }

    public IReadOnlyList<AnalyticsEvent> Read() => _events.ToArray();
}
