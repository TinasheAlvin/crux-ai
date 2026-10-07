using VhonaAI.Core.Calling;

namespace VhonaAI.Core.Entities;

/// <summary>
/// What the owner did with a flag. The flag itself is recomputed from invoices; this row only hides or records it.
/// </summary>
public sealed class CallCustomerAction : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CustomerId { get; set; }
    public CallActionKind Kind { get; set; }
    public DateOnly? SnoozeUntil { get; set; }
    public string? Note { get; set; }
    public string? EvidenceKey { get; set; }
    public Guid ActedByUserId { get; set; }
    public DateTime At { get; set; }

    public Customer Customer { get; set; } = null!;
}
