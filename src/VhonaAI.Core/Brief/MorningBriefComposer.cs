using VhonaAI.Core.Entities;
using VhonaAI.Core.Health;
using VhonaAI.Core.Why;

namespace VhonaAI.Core.Brief;

public static class MorningBriefComposer
{
    public static IReadOnlyList<HealthMetricKind> CandidateMetrics(HealthSnapshot snapshot)
    {
        if (!snapshot.HasData)
        {
            return [];
        }

        var withDelta = snapshot.VisibleCards
            .Where(card => card.Delta.HasValue)
            .OrderByDescending(card => Math.Abs(card.Delta!.Value))
            .ThenBy(card => (int)card.Kind)
            .Select(card => card.Kind);

        var remainder = snapshot.VisibleCards
            .Select(card => card.Kind)
            .Except(withDelta);

        return withDelta.Concat(remainder).Distinct().ToList();
    }

    public static WhyVerification ComposeSingleStory(
        HealthSnapshot snapshot,
        IReadOnlyList<Transaction> transactions)
    {
        if (!snapshot.HasData)
        {
            return WhyVerification.FailClosed(MorningBriefMessages.StoryQuestion);
        }

        foreach (var metric in CandidateMetrics(snapshot))
        {
            var card = snapshot.Card(metric);
            if (card is null)
            {
                continue;
            }

            var verification = WhyVerifier.Verify(card.WhyPrompt, transactions, snapshot, metric);
            if (verification.Verified && verification.Citations.Count > 0)
            {
                return verification;
            }
        }

        return WhyVerification.FailClosed(MorningBriefMessages.StoryQuestion);
    }

    public static MorningBriefSnapshot Capture(HealthSnapshot snapshot)
    {
        if (!snapshot.HasData || snapshot.CurrentPeriod is null)
        {
            return new MorningBriefSnapshot();
        }

        return new MorningBriefSnapshot
        {
            CurrentPeriodLabel = snapshot.CurrentPeriod.Label,
            PreviousPeriodLabel = snapshot.PreviousPeriod?.Label ?? string.Empty,
            Metrics = snapshot.VisibleCards.Select(card => new MorningBriefMetric
            {
                Kind = card.Kind,
                CurrentValue = card.CurrentValue,
                PreviousValue = card.PreviousValue,
                Delta = card.Delta,
                DeltaPercent = card.DeltaPercent
            }).ToList()
        };
    }
}
