using System.Text.Json;
using CruxAI.Core.Health;
using CruxAI.Core.Identity;
using CruxAI.Core.Mapping;
using CruxAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CruxAI.Infrastructure.Health;

public sealed class HealthKpiService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly CruxDbContext _db;
    private readonly ICurrentUser _currentUser;

    public HealthKpiService(CruxDbContext db, ICurrentUser currentUser)
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
