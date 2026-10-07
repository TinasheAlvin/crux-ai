namespace VhonaAI.Core.Entities;

/// <summary>
/// One origin for imported rows (a file today, a connector later).
/// Later sync cursors can live on this row without changing the invoice shape.
/// </summary>
public sealed class DataSource : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DataSourceKind Kind { get; set; }
    public Guid? ImportJobId { get; set; }

    /// <summary>Short system key such as csv, xlsx, or later xero.</summary>
    public string? ExternalSystem { get; set; }

    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public ImportJob? ImportJob { get; set; }
}
