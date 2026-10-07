namespace VhonaAI.Core.Entities;

public sealed class Customer : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? DataSourceId { get; set; }

    /// <summary>Stable id for citations. Same business and normalised name keep the same RowId.</summary>
    public string RowId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    /// <summary>Id in the source system, when the file has one. Cross-source matching is a later phase.</summary>
    public string? SourceExternalId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public DataSource? DataSource { get; set; }
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}
