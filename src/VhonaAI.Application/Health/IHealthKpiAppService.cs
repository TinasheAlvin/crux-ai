using VhonaAI.Core.Health;

namespace VhonaAI.Application.Health;

public interface IHealthKpiAppService
{
    Task<HealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
