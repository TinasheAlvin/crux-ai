namespace VhonaAI.Core.Identity;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    Guid OrganizationId { get; }
    string Email { get; }
    string DisplayName { get; }
    string OrganizationName { get; }

    /// <summary>Owner or Member. Empty when the person has not joined a business yet.</summary>
    string Role => string.Empty;

    bool IsOwner => string.Equals(Role, "Owner", StringComparison.OrdinalIgnoreCase);

    bool HasOrganization
    {
        get
        {
            try
            {
                return OrganizationId != Guid.Empty;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentNullException)
            {
                return false;
            }
        }
    }
}
