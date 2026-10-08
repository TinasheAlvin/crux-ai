using VhonaAI.Core.Analytics;

namespace VhonaAI.Tests;

public class PartnerEventAccessTests
{
    [Fact]
    public void A_business_sees_only_its_own_events()
    {
        var studio = Guid.NewGuid();
        var bakery = Guid.NewGuid();
        var events = new[]
        {
            Event("finishes_upload", studio),
            Event("receipt_open", bakery)
        };

        var allowed = PartnerEventAccess.TryFilter(events, studio, isInternalAdmin: false, out var visible, out var denial);

        Assert.True(allowed);
        Assert.Null(denial);
        Assert.Equal("finishes_upload", Assert.Single(visible).Name);
    }

    [Fact]
    public void An_internal_admin_sees_every_business()
    {
        var events = new[]
        {
            Event("finishes_upload", Guid.NewGuid()),
            Event("receipt_open", Guid.NewGuid())
        };

        var allowed = PartnerEventAccess.TryFilter(events, organizationId: null, isInternalAdmin: true, out var visible, out var denial);

        Assert.True(allowed);
        Assert.Null(denial);
        Assert.Equal(2, visible.Count);
    }

    [Fact]
    public void A_signed_in_user_without_a_business_is_denied()
    {
        var allowed = PartnerEventAccess.TryFilter(
            [Event("finishes_upload", Guid.NewGuid())],
            organizationId: null,
            isInternalAdmin: false,
            out var visible,
            out var denial);

        Assert.False(allowed);
        Assert.Empty(visible);
        Assert.False(string.IsNullOrWhiteSpace(denial));
    }

    private static AnalyticsEvent Event(string name, Guid organizationId) =>
        new()
        {
            Name = name,
            OrgId = organizationId,
            UserId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow
        };
}
