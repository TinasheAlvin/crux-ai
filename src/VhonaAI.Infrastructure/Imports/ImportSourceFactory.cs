using VhonaAI.Core.Entities;
using VhonaAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Infrastructure.Imports;

internal static class ImportSourceFactory
{
    public static async Task<DataSource> GetOrCreateAsync(
        VhonaDbContext db,
        ImportJob job,
        DataSourceKind kind,
        string externalSystem,
        CancellationToken cancellationToken)
    {
        var existing = await db.DataSources
            .FirstOrDefaultAsync(source => source.ImportJobId == job.Id && source.OrganizationId == job.OrganizationId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var source = new DataSource
        {
            Id = Guid.NewGuid(),
            OrganizationId = job.OrganizationId,
            ImportJobId = job.Id,
            Name = job.OriginalFileName,
            Kind = kind,
            ExternalSystem = externalSystem,
            CreatedAt = DateTime.UtcNow
        };
        db.DataSources.Add(source);
        return source;
    }
}
