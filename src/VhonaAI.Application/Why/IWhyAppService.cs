using VhonaAI.Core.Entities;
using VhonaAI.Core.Health;
using VhonaAI.Core.Why;

namespace VhonaAI.Application.Why;

public interface IWhyAppService
{
    Task<WhyAskResult> AskAsync(
        string question,
        HealthMetricKind? seededMetric = null,
        CancellationToken cancellationToken = default);

    Task<WhyAskResult?> GetAsync(Guid answerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WhyCitedRow>> GetReceiptRowsAsync(
        Guid answerId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WhyAnswer>> ListRecentAsync(
        int take = 8,
        CancellationToken cancellationToken = default);
}
