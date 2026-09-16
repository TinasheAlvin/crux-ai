using System.Text.Json;
using CruxAI.Core.Analytics;

namespace CruxAI.Infrastructure.Analytics;

/// <summary>
/// Append-only JSONL sink for local partner demos. One event per line; safe to <c>cat</c> after a session.
/// </summary>
public sealed class JsonlFileEventLog : IEventLog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _path;
    private readonly object _gate = new();

    public JsonlFileEventLog(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Analytics file path is required.", nameof(path));
        }

        _path = System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public string FilePath => _path;

    public void Append(AnalyticsEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var line = JsonSerializer.Serialize(ToRecord(evt), JsonOptions);
        lock (_gate)
        {
            File.AppendAllText(_path, line + Environment.NewLine);
        }
    }

    public IReadOnlyList<AnalyticsEvent> Read()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var events = new List<AnalyticsEvent>();
            foreach (var line in File.ReadAllLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var record = JsonSerializer.Deserialize<EventRecord>(line, JsonOptions);
                if (record is null || string.IsNullOrWhiteSpace(record.Name))
                {
                    continue;
                }

                events.Add(new AnalyticsEvent
                {
                    Name = record.Name,
                    OrgId = record.OrgId,
                    UserId = record.UserId,
                    Timestamp = record.Timestamp,
                    Properties = record.Properties ?? new Dictionary<string, string>()
                });
            }

            return events;
        }
    }

    private static EventRecord ToRecord(AnalyticsEvent evt) => new()
    {
        Name = evt.Name,
        OrgId = evt.OrgId,
        UserId = evt.UserId,
        Timestamp = evt.Timestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(evt.Timestamp, DateTimeKind.Utc)
            : evt.Timestamp.ToUniversalTime(),
        Properties = evt.Properties.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
    };

    private sealed class EventRecord
    {
        public string Name { get; set; } = "";
        public Guid OrgId { get; set; }
        public Guid UserId { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, string>? Properties { get; set; }
    }
}
