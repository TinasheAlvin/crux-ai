namespace CruxAI.Core.Entities;

public sealed class MorningBriefPreference
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }

    public bool OptedIn { get; set; }
    public bool Dismissed { get; set; }
    public DateTime? OptedInAt { get; set; }
    public DateTime? DismissedAt { get; set; }

    public Organization Organization { get; set; } = null!;
}
