using System.Globalization;

namespace VhonaAI.Core.Time;

/// <summary>
/// Display clock for the app and admin console. Values stay stored as UTC.
/// </summary>
public static class JohannesburgTime
{
    public const string ZoneId = "Africa/Johannesburg";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);

    public static DateTime ToLocal(DateTime utc)
    {
        var specified = utc.Kind == DateTimeKind.Utc
            ? utc
            : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(specified, Zone);
    }

    public static string Format(DateTime utc, string format) =>
        ToLocal(utc).ToString(format, CultureInfo.GetCultureInfo("en-ZA"));

    public static string Format(DateTime? utc, string format) =>
        utc is null ? "—" : Format(utc.Value, format);
}
