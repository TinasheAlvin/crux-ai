namespace CruxAI.Core.Entities;

public sealed class MorningBrief
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Calendar day the brief was generated for (UTC).</summary>
    public DateOnly BriefDate { get; set; }

    public string SnapshotJson { get; set; } = "{}";
    public string? CurrentPeriodLabel { get; set; }
    public string? PreviousPeriodLabel { get; set; }

    public string Explanation { get; set; } = string.Empty;
    public bool Verified { get; set; }
    public string? Metric { get; set; }
    public Guid? WhyAnswerId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public WhyAnswer? WhyAnswer { get; set; }
    public ICollection<MorningBriefCitation> Citations { get; set; } = new List<MorningBriefCitation>();
}
