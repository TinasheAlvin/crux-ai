namespace VhonaAI.Core.Analytics;

public sealed class NullAnalytics : IAnalytics
{
    public static NullAnalytics Instance { get; } = new();

    public void Track(string name, IReadOnlyDictionary<string, string>? properties = null)
    {
    }

    public void TrackFinishesUpload(Guid importJobId, int rowCount)
    {
    }

    public void TrackAsksWhySessionOne()
    {
    }

    public void TrackMapAbandon(Guid importJobId)
    {
    }

    public void TrackReceiptOpen(Guid answerId)
    {
    }

    public void TrackTrustRating(Guid answerId, bool trustworthy)
    {
    }

    public void TrackWaitlist(string surface)
    {
    }

    public void MarkOptedInThisSession()
    {
    }

    public void TrackBriefReturnIfEligible(DateTime? optedInAt)
    {
    }
}
