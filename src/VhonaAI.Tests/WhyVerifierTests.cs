using VhonaAI.Core.Brief;
using VhonaAI.Core.Calling;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Health;
using VhonaAI.Core.Why;

namespace VhonaAI.Tests;

public class WhyVerifierTests
{
    [Fact]
    public void Cited_answer_explains_revenue_change_from_persisted_row_ids()
    {
        var transactions = new List<Transaction>
        {
            Row("r-feb-in", new DateOnly(2026, 2, 14), 2800m, "Card machine settlement"),
            Row("r-feb-out", new DateOnly(2026, 2, 10), -2000m, "Colour"),
            Row("r-mar-in", new DateOnly(2026, 3, 5), 3500m, "Card machine settlement"),
            Row("r-mar-out", new DateOnly(2026, 3, 8), -2700m, "Stock")
        };
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);
        var question = snapshot.Revenue!.WhyPrompt;

        var result = WhyVerifier.Verify(question, transactions, snapshot);

        Assert.True(result.Verified);
        Assert.Equal(HealthMetricKind.Revenue, result.Metric);
        Assert.Contains("r-mar-in", result.Citations.Select(c => c.RowId));
        Assert.Contains("r-feb-in", result.Citations.Select(c => c.RowId));
        Assert.DoesNotContain("r-mar-out", result.Citations.Select(c => c.RowId));
        Assert.DoesNotContain("r-feb-out", result.Citations.Select(c => c.RowId));
        Assert.Contains("Revenue is up R700, 25% above February.", result.Answer);
        Assert.Contains("Card machine settlement is up R700.", result.Answer);
        Assert.Equal(700m, result.Reasons.Sum(reason => reason.Amount));
        Assert.All(result.Citations, citation =>
        {
            Assert.Contains("Date", citation.Columns);
            Assert.Contains("Description", citation.Columns);
            Assert.Contains("Amount", citation.Columns);
        });

        var cited = transactions.Where(t => result.Citations.Any(c => c.RowId == t.RowId)).ToList();
        Assert.Equal(3500m + 2800m, cited.Sum(t => t.Amount));
        Assert.NotEqual(WhyMessages.Unverified, result.Answer);
    }

    [Fact]
    public void Fail_closed_when_the_question_has_no_verifiable_metric()
    {
        var transactions = new[]
        {
            Row("r-mar-in", new DateOnly(2026, 3, 5), 3500m, "Card machine settlement")
        };
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);

        var result = WhyVerifier.Verify("Why is the weather nicer this week?", transactions, snapshot);

        Assert.False(result.Verified);
        Assert.Equal(WhyMessages.Unverified, result.Answer);
        Assert.Empty(result.Citations);
        Assert.DoesNotContain("3", result.Answer);
        Assert.DoesNotContain("3500", result.Answer);
    }

    [Fact]
    public void Fail_closed_for_cash_when_cash_is_hidden()
    {
        var transactions = new[]
        {
            Row("r1", new DateOnly(2026, 2, 10), 1000m, "Inflow", 5000m),
            Row("r2", new DateOnly(2026, 3, 5), 800m, "Inflow", 5800m)
        };
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);

        var result = WhyVerifier.Verify("Why did cash change?", transactions, snapshot);

        Assert.False(result.Verified);
        Assert.Equal(WhyMessages.Unverified, result.Answer);
        Assert.Empty(result.Citations);
    }

    [Fact]
    public void Fail_closed_when_a_change_cannot_cite_both_periods()
    {
        var transactions = new[]
        {
            Row("r-mar-in", new DateOnly(2026, 3, 5), 1200m, "Cut")
        };
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);

        var result = WhyVerifier.Verify("Why did revenue change?", transactions, snapshot);

        Assert.False(result.Verified);
        Assert.Empty(result.Citations);
        Assert.Equal(WhyMessages.Unverified, result.Answer);
    }

    [Fact]
    public void Seeded_metric_is_used_when_the_question_says_this()
    {
        var transactions = new[]
        {
            Row("r-feb-out", new DateOnly(2026, 2, 10), -400m, "Fees"),
            Row("r-mar-out", new DateOnly(2026, 3, 8), -900m, "Stock")
        };
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);

        var result = WhyVerifier.Verify(
            "Why did this change?",
            transactions,
            snapshot,
            seededMetric: HealthMetricKind.Expenses);

        Assert.True(result.Verified);
        Assert.Equal(HealthMetricKind.Expenses, result.Metric);
        Assert.Contains("r-mar-out", result.Citations.Select(c => c.RowId));
        Assert.Contains("r-feb-out", result.Citations.Select(c => c.RowId));
        Assert.Contains(RandAmounts.Format(900m), result.Answer);
    }

    [Fact]
    public void Cash_answer_cites_balance_rows()
    {
        var transactions = new[]
        {
            Row("r-feb", new DateOnly(2026, 2, 28), -50m, "Fees", 12_000m),
            Row("r-mar-1", new DateOnly(2026, 3, 2), 100m, "Inflow", 12_100m),
            Row("r-mar-2", new DateOnly(2026, 3, 20), -300m, "Stock", 11_800m)
        };
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: true);

        var result = WhyVerifier.Verify(snapshot.Cash!.WhyPrompt, transactions, snapshot);

        Assert.True(result.Verified);
        Assert.Contains("r-mar-2", result.Citations.Select(c => c.RowId));
        Assert.Contains("r-feb", result.Citations.Select(c => c.RowId));
        Assert.All(result.Citations, citation => Assert.Contains("Balance", citation.Columns));
        Assert.Contains("Cash is down R200, 1.7% below February.", result.Answer);
        Assert.Equal(-200m, result.Reasons.Sum(reason => reason.Amount));
    }

    [Fact]
    public void Cited_answer_does_not_embed_row_ids_that_belong_on_the_receipt()
    {
        var transactions = new List<Transaction>
        {
            Row("r-feb-in", new DateOnly(2026, 2, 14), 2800m, "Card machine settlement"),
            Row("r-mar-in", new DateOnly(2026, 3, 5), 3500m, "Card machine settlement")
        };
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);

        var result = WhyVerifier.Verify(snapshot.Revenue!.WhyPrompt, transactions, snapshot);

        Assert.True(result.Verified);
        Assert.DoesNotContain("r-feb-in", result.Answer, StringComparison.Ordinal);
        Assert.DoesNotContain("r-mar-in", result.Answer, StringComparison.Ordinal);
        Assert.Contains("r-feb-in", result.Citations.Select(c => c.RowId));
        Assert.Contains("r-mar-in", result.Citations.Select(c => c.RowId));
    }

    [Fact]
    public void Why_and_brief_text_format_rand_like_the_cards()
    {
        var transactions = new List<Transaction>
        {
            Row("r-feb-in", new DateOnly(2026, 2, 14), 4850m, "February sales"),
            Row("r-mar-in", new DateOnly(2026, 3, 5), 3200.50m, "March sales")
        };
        var snapshot = HealthKpiCalculator.Compute(transactions, cashFieldMapped: false);

        var result = WhyVerifier.Verify("Why did revenue change?", transactions, snapshot);
        var story = MorningBriefComposer.ComposeSingleStory(snapshot, transactions);

        Assert.True(result.Verified);
        Assert.Contains("Revenue is down R1 649.50, 34% below February.", result.Answer);
        Assert.Contains("down R1 649.50", result.Answer);
        Assert.DoesNotContain("R4,850.00", result.Answer);
        Assert.DoesNotContain(".00", result.Answer);
        Assert.Contains("Revenue is down R1 649.50, 34% below February.", story.Answer);
        Assert.Contains("R4 850", story.Answer);
        Assert.DoesNotContain("R4,850", story.Answer);
        Assert.DoesNotContain("R1,649", story.Answer);
        Assert.Contains("from R4 850 in ", snapshot.Revenue!.WhyPrompt);
        Assert.Contains("to R3 200.50 in ", snapshot.Revenue.WhyPrompt);
    }

    [Fact]
    public void Never_returns_a_verified_answer_without_citations()
    {
        var empty = WhyVerifier.Verify("Why did revenue change?", [], new HealthSnapshot { HasData = false });
        Assert.False(empty.Verified);
        Assert.Empty(empty.Citations);
        Assert.Equal(WhyMessages.Unverified, empty.Answer);
    }

    private static Transaction Row(
        string rowId,
        DateOnly date,
        decimal amount,
        string description,
        decimal? balance = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            RowId = rowId,
            Date = date,
            Amount = amount,
            Description = description,
            Balance = balance,
            SourceRowNumber = 1
        };
}
