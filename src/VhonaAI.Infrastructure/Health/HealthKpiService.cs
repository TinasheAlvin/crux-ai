using System.Text.Json;
using VhonaAI.Application.Health;
using VhonaAI.Core.Health;
using VhonaAI.Core.Identity;
using VhonaAI.Core.Mapping;
using VhonaAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Infrastructure.Health;

public sealed class HealthKpiService : IHealthKpiAppService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly VhonaDbContext _db;
    private readonly ICurrentUser _currentUser;

    public HealthKpiService(VhonaDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<HealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return new HealthSnapshot { HasData = false };
        }

        var transactions = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.OrganizationId == _currentUser.OrganizationId)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.SourceRowNumber)
            .ToListAsync(cancellationToken);

        var latestJob = await _db.ImportJobs
            .AsNoTracking()
            .Where(j => j.OrganizationId == _currentUser.OrganizationId && j.Status == Core.Entities.ImportStatus.Imported)
            .OrderByDescending(j => j.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var mapping = DeserializeMapping(latestJob?.MappingJson);
        var cashMapped = HealthKpiCalculator.MappingIncludesCashField(mapping);

        return HealthKpiCalculator.Compute(transactions, cashMapped, latestJob?.Id);
    }

    private static IReadOnlyDictionary<string, string> DeserializeMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
               ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
