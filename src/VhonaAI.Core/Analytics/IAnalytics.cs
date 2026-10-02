namespace VhonaAI.Core.Analytics;

/// <summary>
/// Partner scoreboard tracker. Implementations stamp orgId, userId, and timestamp then append to <see cref="IEventLog"/>.
/// </summary>
public interface IAnalytics
{
    void Track(string name, IReadOnlyDictionary<string, string>? properties = null);

    void TrackFinishesUpload(Guid importJobId, int rowCount);

    void TrackAsksWhySessionOne();

    void TrackMapAbandon(Guid importJobId);

    void TrackReceiptOpen(Guid answerId);

    void TrackTrustRating(Guid answerId, bool trustworthy);

    void TrackWaitlist(string surface);

    void MarkOptedInThisSession();

    void TrackBriefReturnIfEligible(DateTime? optedInAt);
}
