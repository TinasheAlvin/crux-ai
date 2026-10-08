namespace VhonaAI.Core.Entities;

/// <summary>
/// Operator record of an admin view or change. It is not owned by a business, so a POPIA delete
/// of that business does not erase the record of who deleted it.
/// </summary>
public sealed class AdminAuditEntry
{
    public Guid Id { get; set; }
    public DateTime At { get; set; }
    public Guid ActorUserId { get; set; }
    public string ActorEmail { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public Guid? OrganizationId { get; set; }
    public string? Target { get; set; }
    public string Summary { get; set; } = string.Empty;
}
