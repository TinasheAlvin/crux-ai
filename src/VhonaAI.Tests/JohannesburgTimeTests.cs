using VhonaAI.Core.Time;

namespace VhonaAI.Tests;

public class JohannesburgTimeTests
{
    [Fact]
    public void Utc_and_unspecified_instants_display_in_south_african_time()
    {
        var utc = new DateTime(2026, 10, 7, 22, 30, 0, DateTimeKind.Utc);
        var unspecified = new DateTime(2026, 10, 7, 22, 30, 0, DateTimeKind.Unspecified);

        Assert.Equal("2026-10-08 00:30", JohannesburgTime.Format(utc, "yyyy-MM-dd HH:mm"));
        Assert.Equal("2026-10-08 00:30", JohannesburgTime.Format(unspecified, "yyyy-MM-dd HH:mm"));
        Assert.Equal("—", JohannesburgTime.Format(null, "yyyy-MM-dd HH:mm"));
        Assert.Equal(new DateTime(2026, 10, 7, 22, 30, 0, DateTimeKind.Utc), utc);
    }
}
