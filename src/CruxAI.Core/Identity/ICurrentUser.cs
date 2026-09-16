namespace CruxAI.Core.Identity;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    Guid OrganizationId { get; }
    string Email { get; }
    string DisplayName { get; }
    string OrganizationName { get; }
}
