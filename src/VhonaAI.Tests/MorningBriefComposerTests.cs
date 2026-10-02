using VhonaAI.Core.Brief;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Health;
using VhonaAI.Core.Why;

namespace VhonaAI.Tests;

public class MorningBriefComposerTests
{
    [Fact]
    public void Compose_picks_a_single_cited_story_not_a_digest()
    {
        var transactions = new List<Transaction>
        {
            Row("r-feb-in", new DateOnly(2026, 2, 3), 2800m),
            Row("r-feb-out", new DateOnly(2026, 2, 10), -2000m),
            Row("r-mar-in", new DateOnly(2026, 3, 2), 3500m),
            Row("r-mar-out", new DateOnly(2026, 3, 8), -2700m)
        };

        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);
        var story = MorningBriefComposer.ComposeSingleStory(snapshot, transactions);

        Assert.True(story.Verified);
        Assert.NotNull(story.Metric);
        Assert.NotEmpty(story.Citations);
        Assert.False(string.IsNullOrWhiteSpace(story.Answer));
        Assert.Equal(1, new[] { "Revenue", "Expenses", "Profit" }.Count(name =>
            story.Answer.StartsWith(name, StringComparison.Ordinal)));
    }

    [Fact]
    public void Compose_fail_closes_without_inventing_a_digest()
    {
        var snapshot = HealthKpiCalculator.Compute([], cashFieldMapped: false);
        var story = MorningBriefComposer.ComposeSingleStory(snapshot, []);

        Assert.False(story.Verified);
        Assert.Empty(story.Citations);
        Assert.Equal(WhyMessages.Unverified, story.Answer);
    }

    [Fact]
    public void Empty_hint_names_vhona_and_does_not_invent_a_digest()
    {
        Assert.Equal(
            "Ask why for a cited answer. Vhona never fills a brief with a guessed digest.",
            MorningBriefMessages.EmptyHint);
    }

    [Fact]
    public void Capture_freezes_visible_kpi_snapshot_metrics()
    {
        var transactions = new List<Transaction>
        {
            Row("r-feb-in", new DateOnly(2026, 2, 3), 2800m),
            Row("r-mar-in", new DateOnly(2026, 3, 2), 3500m)
        };
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);
        var captured = MorningBriefComposer.Capture(snapshot);

        Assert.Equal("Mar 2026", captured.CurrentPeriodLabel);
        Assert.Equal("Feb 2026", captured.PreviousPeriodLabel);
        Assert.Contains(captured.Metrics, metric => metric.Kind == HealthMetricKind.Revenue && metric.CurrentValue == 3500m);
        Assert.DoesNotContain(captured.Metrics, metric => metric.Kind == HealthMetricKind.Cash);
    }

    private static Transaction Row(string rowId, DateOnly date, decimal amount) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ImportJobId = Guid.NewGuid(),
            RowId = rowId,
            SourceRowNumber = 1,
            Date = date,
            Description = rowId,
            Amount = amount,
            Currency = "ZAR",
            ImportedAt = DateTime.UtcNow
        };
}
