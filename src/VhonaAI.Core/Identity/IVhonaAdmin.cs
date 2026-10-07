namespace VhonaAI.Core.Identity;

/// <summary>
/// Platform operator. This is not a business Owner or Member, and a business owner cannot grant it.
/// </summary>
public interface IVhonaAdmin
{
    bool IsAdmin { get; }
    Guid UserId { get; }
    string Email { get; }
    string? ObjectId { get; }
}
