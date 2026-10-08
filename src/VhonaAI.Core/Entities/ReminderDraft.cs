using VhonaAI.Core.Calling;

namespace VhonaAI.Core.Entities;

/// <summary>
/// An owner-edited late reminder. Status stays "Not sent": Vhona never sends it.
/// </summary>
public sealed class ReminderDraft : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CustomerId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool EditedByOwner { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Status { get; set; } = ReminderDraftTemplate.NotSent;

    public Customer Customer { get; set; } = null!;
}
