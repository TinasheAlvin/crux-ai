using CruxAI.Core.Analytics;
using CruxAI.Core.Identity;
using CruxAI.Core.Time;

namespace CruxAI.Infrastructure.Analytics;

public sealed class AnalyticsService : IAnalytics
{
    private readonly IEventLog _log;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    private readonly PartnerSession _session;

    public AnalyticsService(IEventLog log, ICurrentUser user, IClock clock, PartnerSession session)
    {
        _log = log;
        _user = user;
        _clock = clock;
        _session = session;
    }

    public void Track(string name, IReadOnlyDictionary<string, string>? properties = null)
    {
        if (string.IsNullOrWhiteSpace(name) || !_user.IsAuthenticated)
        {
            return;
        }

        _log.Append(new AnalyticsEvent
        {
            Name = name,
            OrgId = _user.OrganizationId,
            UserId = _user.UserId,
            Timestamp = _clock.UtcNow,
            Properties = properties ?? new Dictionary<string, string>()
        });
    }

    public void TrackFinishesUpload(Guid importJobId, int rowCount) =>
        Track(PartnerEventNames.FinishesUpload, Props(
            ("importJobId", importJobId.ToString()),
            ("rowCount", rowCount.ToString())));

    public void TrackAsksWhySessionOne()
    {
        if (!_session.TryMarkFirstWhyAsk())
        {
            return;
        }

        Track(PartnerEventNames.AsksWhySessionOne);
    }

    public void TrackMapAbandon(Guid importJobId) =>
        Track(PartnerEventNames.MapAbandon, Props(("importJobId", importJobId.ToString())));

    public void TrackReceiptOpen(Guid answerId)
    {
        if (answerId == Guid.Empty || !_session.OpenedReceipts.Add(answerId))
        {
            return;
        }

        Track(PartnerEventNames.ReceiptOpen, Props(("answerId", answerId.ToString())));
    }

    public void TrackTrustRating(Guid answerId, bool trustworthy)
    {
        if (answerId == Guid.Empty || !_session.RatedAnswers.Add(answerId))
        {
            return;
        }

        var name = trustworthy
            ? PartnerEventNames.RatesExplanationTrustworthy
            : PartnerEventNames.ReceiptDistrust;
        Track(name, Props(("answerId", answerId.ToString())));
    }

    public void TrackWaitlist(string surface)
    {
        if (_session.WaitlistSignaled)
        {
            return;
        }

        _session.WaitlistSignaled = true;
        Track(PartnerEventNames.PayOrWaitlistSignal, Props(("surface", surface)));
    }

    public void MarkOptedInThisSession() => _session.OptedInThisSession = true;

    public void TrackBriefReturnIfEligible(DateTime? optedInAt)
    {
        if (_session.BriefReturnTracked || _session.OptedInThisSession || optedInAt is null)
        {
            return;
        }

        var optedIn = SpecifyUtc(optedInAt.Value);
        var elapsed = _clock.UtcNow - optedIn;
        if (elapsed < TimeSpan.Zero || elapsed > PartnerEventNames.BriefReturnWindow)
        {
            return;
        }

        _session.BriefReturnTracked = true;
        Track(PartnerEventNames.ReturnsForBriefWithin7Days, Props(
            ("optedInAt", optedIn.ToString("O")),
            ("daysSinceOptIn", elapsed.TotalDays.ToString("0.###"))));
    }

    private static IReadOnlyDictionary<string, string> Props(params (string Key, string Value)[] pairs)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            map[key] = value;
        }

        return map;
    }

    private static DateTime SpecifyUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
