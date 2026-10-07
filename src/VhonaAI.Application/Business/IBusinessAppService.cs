namespace VhonaAI.Application.Business;

public interface IBusinessAppService
{
    Task<BusinessMembership?> FindMembershipAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<BusinessMembership> CreateBusinessAsync(Guid userId, string name, CancellationToken cancellationToken = default);

    Task<BusinessMembership> RenameAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BusinessMember>> ListMembersAsync(CancellationToken cancellationToken = default);

    Task<InvitationIssued> InviteMemberAsync(string email, CancellationToken cancellationToken = default);

    Task RevokeInviteAsync(Guid invitationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingInvitation>> ListPendingInvitesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingInvitation>> ListInvitesForCurrentEmailAsync(CancellationToken cancellationToken = default);

    Task<BusinessMembership> AcceptInviteAsync(string token, CancellationToken cancellationToken = default);
}

public sealed record BusinessMembership(Guid UserId, Guid OrganizationId, string OrganizationName, string Role);

public sealed record BusinessMember(Guid UserId, string Email, string DisplayName, string Role);

public sealed record InvitationIssued(Guid Id, string Email, string Token, DateTime ExpiresAt, string OrganizationName);

public sealed record PendingInvitation(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string Email,
    string Token,
    DateTime ExpiresAt);
