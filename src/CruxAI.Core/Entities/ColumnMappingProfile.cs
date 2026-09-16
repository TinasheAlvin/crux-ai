namespace CruxAI.Core.Entities;

/// <summary>Reusable org-level header mapping, applied on the next upload when headers match.</summary>
public sealed class ColumnMappingProfile
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "Default";
    public string MappingJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
}
