namespace VhonaAI.Core.Analytics;

/// <summary>
/// Partner events are an internal dump. A signed-in owner or member sees only their business.
/// An explicit internal-admin claim can see every business. There is no anonymous access.
/// </summary>
public static class PartnerEventAccess
{
    public static bool TryFilter(
        IReadOnlyList<AnalyticsEvent> events,
        Guid? organizationId,
        bool isInternalAdmin,
        out IReadOnlyList<AnalyticsEvent> visible,
        out string? denial)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (isInternalAdmin)
        {
            visible = events;
            denial = null;
            return true;
        }

        if (organizationId is null || organizationId == Guid.Empty)
        {
            visible = Array.Empty<AnalyticsEvent>();
            denial = "Sign in to a business to view partner events.";
            return false;
        }

        visible = events.Where(evt => evt.OrgId == organizationId.Value).ToArray();
        denial = null;
        return true;
    }
}
