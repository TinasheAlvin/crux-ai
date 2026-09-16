namespace CruxAI.Core.Entities;

public sealed class WhyAnswer
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? AskedByUserId { get; set; }

    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public bool Verified { get; set; }
    public string? Metric { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public ICollection<WhyCitation> Citations { get; set; } = new List<WhyCitation>();
}
