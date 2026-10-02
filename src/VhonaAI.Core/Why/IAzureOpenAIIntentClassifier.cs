using VhonaAI.Core.Health;

namespace VhonaAI.Core.Why;

/// <summary>
/// Optional Azure OpenAI hook. May classify which KPI a question is about.
/// Must never author financial facts — the verifier still has to cite RowIds.
/// </summary>
public interface IAzureOpenAIIntentClassifier
{
    bool IsConfigured { get; }

    Task<HealthMetricKind?> TryClassifyAsync(string question, CancellationToken cancellationToken = default);
}
