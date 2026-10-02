namespace VhonaAI.Core.Entities;

public sealed class Membership
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public MembershipRole Role { get; set; } = MembershipRole.Owner;
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public AppUser User { get; set; } = null!;
}

public enum MembershipRole
{
    Owner = 0,
    Member = 1
}
