using System.Text.Json;
using CruxAI.Core.Analytics;
using Microsoft.Data.Sqlite;

namespace CruxAI.Infrastructure.Analytics;

/// <summary>
/// Append-only SQLite sink for local partner demos. Separate from the product DB so dumps stay simple.
/// </summary>
public sealed class SqliteEventLog : IEventLog, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly object _gate = new();
    private readonly SqliteConnection _connection;

    public SqliteEventLog(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Analytics SQLite path is required.", nameof(path));
        }

        _path = System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection($"Data Source={_path}");
        _connection.Open();
        using var create = _connection.CreateCommand();
        create.CommandText =
            """
            CREATE TABLE IF NOT EXISTS partner_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                org_id TEXT NOT NULL,
                user_id TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                properties_json TEXT NOT NULL
            );
            """;
        create.ExecuteNonQuery();
    }

    public string FilePath => _path;

    public void Append(AnalyticsEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var timestamp = evt.Timestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(evt.Timestamp, DateTimeKind.Utc)
            : evt.Timestamp.ToUniversalTime();
        var properties = JsonSerializer.Serialize(
            evt.Properties.ToDictionary(pair => pair.Key, pair => pair.Value),
            JsonOptions);

        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO partner_events (name, org_id, user_id, timestamp, properties_json)
                VALUES ($name, $org, $user, $ts, $props);
                """;
            command.Parameters.AddWithValue("$name", evt.Name);
            command.Parameters.AddWithValue("$org", evt.OrgId.ToString());
            command.Parameters.AddWithValue("$user", evt.UserId.ToString());
            command.Parameters.AddWithValue("$ts", timestamp.ToString("O"));
            command.Parameters.AddWithValue("$props", properties);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<AnalyticsEvent> Read()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT name, org_id, user_id, timestamp, properties_json FROM partner_events ORDER BY id;";
            using var reader = command.ExecuteReader();
            var events = new List<AnalyticsEvent>();
            while (reader.Read())
            {
                var propsJson = reader.GetString(4);
                var props = JsonSerializer.Deserialize<Dictionary<string, string>>(propsJson, JsonOptions)
                            ?? new Dictionary<string, string>();
                events.Add(new AnalyticsEvent
                {
                    Name = reader.GetString(0),
                    OrgId = Guid.Parse(reader.GetString(1)),
                    UserId = Guid.Parse(reader.GetString(2)),
                    Timestamp = DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    Properties = props
                });
            }

            return events;
        }
    }

    public void Dispose() => _connection.Dispose();
}
