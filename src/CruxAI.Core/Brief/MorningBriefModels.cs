using CruxAI.Core.Health;
using CruxAI.Core.Why;

namespace CruxAI.Core.Brief;

public static class MorningBriefMessages
{
    public const string Cta = "Send me the morning brief";
    public const string Dismiss = "Not now";
    public const string Empty = "No verified brief today";
    public const string EmptyHint = "Ask why for a cited answer. Crux never fills a brief with a guessed digest.";
    public const string AskWhy = "Ask why";
    public const string SheetTitle = "Get a morning brief";
    public const string SheetLede = "Yesterday’s snapshot plus one cited explanation. Never a multi-story digest.";
    public const string OptedIn = "You’re in. Next visit you’ll get yesterday’s snapshot and one receipted explanation.";
    public const string SeeBrief = "See morning brief";
    public const string OptInRequired = "Opt-in is only available after a trusted (cited) why.";
    public const string AlreadyDismissed = "The morning brief offer was already dismissed.";
    public const string StoryQuestion = "What changed since last close?";
}

public sealed class MorningBriefMetric
{
    public HealthMetricKind Kind { get; init; }
    public decimal CurrentValue { get; init; }
    public decimal? PreviousValue { get; init; }
    public decimal? Delta { get; init; }
    public decimal? DeltaPercent { get; init; }
}

public sealed class MorningBriefSnapshot
{
    public string CurrentPeriodLabel { get; init; } = string.Empty;
    public string PreviousPeriodLabel { get; init; } = string.Empty;
    public IReadOnlyList<MorningBriefMetric> Metrics { get; init; } = [];
}

public sealed class MorningBriefPreferenceState
{
    public bool HasTrustedWhy { get; init; }
    public bool OptedIn { get; init; }
    public bool Dismissed { get; init; }
    public DateTime? OptedInAt { get; init; }

    public bool ShowOptInSheet => HasTrustedWhy && !OptedIn && !Dismissed;
}

public sealed class MorningBriefOptInResult
{
    public required bool Succeeded { get; init; }
    public required MorningBriefPreferenceState Preference { get; init; }
    public string? Error { get; init; }
}

public sealed class MorningBriefLanding
{
    public required bool OptedIn { get; init; }
    public bool Verified { get; init; }
    public Guid? BriefId { get; init; }
    public DateOnly? BriefDate { get; init; }
    public MorningBriefSnapshot? Snapshot { get; init; }
    public string Explanation { get; init; } = string.Empty;
    public HealthMetricKind? Metric { get; init; }
    public WhyAskResult? Receipt { get; init; }

    public static MorningBriefLanding NotOptedIn() => new() { OptedIn = false };

    public static MorningBriefLanding FailClosed(Guid briefId, DateOnly briefDate, MorningBriefSnapshot? snapshot) =>
        new()
        {
            OptedIn = true,
            Verified = false,
            BriefId = briefId,
            BriefDate = briefDate,
            Snapshot = snapshot,
            Explanation = string.Empty,
            Receipt = null
        };
}
