namespace VhonaAI.Core.Entities;

public sealed class Invitation : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }

    /// <summary>Normalised email the invite is for.</summary>
    public string Email { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;
    public MembershipRole Role { get; set; } = MembershipRole.Member;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public Guid? AcceptedByUserId { get; set; }
    public DateTime? RevokedAt { get; set; }

    public Organization Organization { get; set; } = null!;
}
