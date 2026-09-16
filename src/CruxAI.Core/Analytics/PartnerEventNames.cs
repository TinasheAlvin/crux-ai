namespace CruxAI.Core.Analytics;

/// <summary>
/// Design-partner scoreboard event names. Stable strings — do not rename without a partner dump note.
/// </summary>
public static class PartnerEventNames
{
    public const string FinishesUpload = "finishes_upload";
    public const string AsksWhySessionOne = "asks_why_session_one";
    public const string RatesExplanationTrustworthy = "rates_explanation_trustworthy";
    public const string ReturnsForBriefWithin7Days = "returns_for_brief_within_7_days";
    public const string PayOrWaitlistSignal = "pay_or_waitlist_signal";
    public const string MapAbandon = "map_abandon";
    public const string ReceiptOpen = "receipt_open";
    public const string ReceiptDistrust = "receipt_distrust";

    public static readonly TimeSpan BriefReturnWindow = TimeSpan.FromDays(7);

    public static IReadOnlyList<string> All { get; } =
    [
        FinishesUpload,
        AsksWhySessionOne,
        RatesExplanationTrustworthy,
        ReturnsForBriefWithin7Days,
        PayOrWaitlistSignal,
        MapAbandon,
        ReceiptOpen,
        ReceiptDistrust
    ];
}
