namespace VhonaAI.Core.Entities;

public sealed class AppUser
{
    public Guid Id { get; set; }

    /// <summary>
    /// Entra External ID object id when real auth is wired. Demo auth uses a stable local id.
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>Set when a Vhona admin disables this sign-in. Null means the person can use the app.</summary>
    public DateTime? DisabledAt { get; set; }

    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
}
