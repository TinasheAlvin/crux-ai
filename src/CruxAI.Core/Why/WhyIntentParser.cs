using System.Text.RegularExpressions;
using CruxAI.Core.Health;

namespace CruxAI.Core.Why;

public static class WhyIntentParser
{
    private static readonly (string Needle, HealthMetricKind Kind)[] Metrics =
    [
        ("running balance", HealthMetricKind.Cash),
        ("bank balance", HealthMetricKind.Cash),
        ("closing balance", HealthMetricKind.Cash),
        ("revenue", HealthMetricKind.Revenue),
        ("sales", HealthMetricKind.Revenue),
        ("inflow", HealthMetricKind.Revenue),
        ("income", HealthMetricKind.Revenue),
        ("expenses", HealthMetricKind.Expenses),
        ("expense", HealthMetricKind.Expenses),
        ("spending", HealthMetricKind.Expenses),
        ("outflow", HealthMetricKind.Expenses),
        ("costs", HealthMetricKind.Expenses),
        ("cost", HealthMetricKind.Expenses),
        ("profit", HealthMetricKind.Profit),
        ("surplus", HealthMetricKind.Profit),
        ("cash", HealthMetricKind.Cash)
    ];

    private static readonly string[] ChangeWords =
    [
        "change", "changed", "changes",
        "increase", "increased", "decrease", "decreased",
        "drop", "dropped", "fell", "fall", "rose", "rise",
        "growth", "grow", "decline", "declined",
        "versus", "compared", "comparison",
        "delta", "difference", "moved", "movement"
    ];

    public static WhyIntent Parse(string question, HealthMetricKind? seededMetric = null)
    {
        var text = Normalize(question);
        var named = ParseMetric(text);
        var metric = named ?? seededMetric;
        var wantsChange = ContainsAnyWord(text, ChangeWords)
                          || ContainsWord(text, "vs")
                          || ContainsWord(text, "up")
                          || ContainsWord(text, "down");

        return new WhyIntent
        {
            Metric = metric,
            WantsChange = wantsChange
        };
    }

    public static HealthMetricKind? ParseMetric(string question)
    {
        var text = Normalize(question);
        foreach (var (needle, kind) in Metrics)
        {
            if (ContainsPhrase(text, needle))
            {
                return kind;
            }
        }

        return null;
    }

    public static string Normalize(string question) =>
        Regex.Replace(question.Trim().ToLowerInvariant(), @"\s+", " ");

    private static bool ContainsPhrase(string text, string phrase)
    {
        if (phrase.Contains(' ', StringComparison.Ordinal))
        {
            return text.Contains(phrase, StringComparison.Ordinal);
        }

        return ContainsWord(text, phrase);
    }

    private static bool ContainsAnyWord(string text, IEnumerable<string> words) =>
        words.Any(word => ContainsWord(text, word));

    private static bool ContainsWord(string text, string word) =>
        Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
