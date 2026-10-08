using System.Security.Cryptography;
using VhonaAI.Application.Business;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Identity;
using VhonaAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Infrastructure.Identity;

public sealed class BusinessService : IBusinessAppService
{
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(14);

    private readonly VhonaDbContext _db;
    private readonly ICurrentUser _currentUser;

    public BusinessService(VhonaDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<BusinessMembership?> FindMembershipAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var membership = await Memberships()
            .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        return membership is null ? null : ToMembership(membership);
    }

    public async Task<BusinessMembership> CreateBusinessAsync(
        Guid userId,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.IsAuthenticated && _currentUser.UserId != userId)
        {
            throw new InvalidOperationException("You can only name your own business.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("Sign in before naming a business.");

        var existing = await Memberships().AnyAsync(item => item.UserId == user.Id, cancellationToken);
        if (existing)
        {
            throw new InvalidOperationException("This sign-in already belongs to a business.");
        }

        var businessName = BusinessNameRules.Normalize(name);
        var now = DateTime.UtcNow;
        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = businessName,
            CreatedAt = now
        };
        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            UserId = user.Id,
            Role = MembershipRole.Owner,
            CreatedAt = now
        };
        _db.Organizations.Add(organization);
        _db.Memberships.Add(membership);
        await _db.SaveChangesAsync(cancellationToken);
        membership.Organization = organization;
        return ToMembership(membership);
    }

    public async Task<BusinessMembership> RenameAsync(string name, CancellationToken cancellationToken = default)
    {
        RequireOwner();
        var businessName = BusinessNameRules.Normalize(name);
        var organization = await _db.Organizations
            .FirstOrDefaultAsync(item => item.Id == _currentUser.OrganizationId, cancellationToken)
            ?? throw new InvalidOperationException("Business not found.");
        organization.Name = businessName;
        await _db.SaveChangesAsync(cancellationToken);
        return new BusinessMembership(_currentUser.UserId, organization.Id, organization.Name, _currentUser.Role);
    }

    public async Task<IReadOnlyList<BusinessMember>> ListMembersAsync(CancellationToken cancellationToken = default)
    {
        RequireMember();
        var members = await _db.Memberships
            .Include(item => item.User)
            .Where(item => item.OrganizationId == _currentUser.OrganizationId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        return members
            .Select(item => new BusinessMember(item.UserId, item.User.Email, item.User.DisplayName, item.Role.ToString()))
            .ToList();
    }

    public async Task<InvitationIssued> InviteMemberAsync(string email, CancellationToken cancellationToken = default)
    {
        RequireOwner();
        var normalised = NormalizeEmail(email);
        if (normalised.Length == 0 || !normalised.Contains('@'))
        {
            throw new InvalidOperationException("Enter an email address to invite.");
        }

        var alreadyMember = await _db.Memberships
            .Include(item => item.User)
            .AnyAsync(
                item => item.OrganizationId == _currentUser.OrganizationId && item.User.Email.ToLower() == normalised,
                cancellationToken);
        if (alreadyMember)
        {
            throw new InvalidOperationException("That person is already in this business.");
        }

        var now = DateTime.UtcNow;
        var pending = await _db.Invitations.FirstOrDefaultAsync(
            item => item.OrganizationId == _currentUser.OrganizationId
                    && item.Email == normalised
                    && item.AcceptedAt == null
                    && item.RevokedAt == null
                    && item.ExpiresAt > now,
            cancellationToken);
        if (pending is null)
        {
            pending = new Invitation
            {
                Id = Guid.NewGuid(),
                OrganizationId = _currentUser.OrganizationId,
                Email = normalised,
                Token = NewToken(),
                Role = MembershipRole.Member,
                CreatedByUserId = _currentUser.UserId,
                CreatedAt = now
            };
            _db.Invitations.Add(pending);
        }

        pending.ExpiresAt = now.Add(InviteLifetime);
        await _db.SaveChangesAsync(cancellationToken);
        return new InvitationIssued(
            pending.Id,
            pending.Email,
            pending.Token,
            pending.ExpiresAt,
            _currentUser.OrganizationName);
    }

    public async Task RevokeInviteAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        RequireOwner();
        var invite = await _db.Invitations.FirstOrDefaultAsync(
            item => item.Id == invitationId && item.OrganizationId == _currentUser.OrganizationId,
            cancellationToken)
            ?? throw new InvalidOperationException("Invite not found.");
        if (invite.AcceptedAt is not null)
        {
            throw new InvalidOperationException("That invite was already accepted.");
        }

        invite.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PendingInvitation>> ListPendingInvitesAsync(CancellationToken cancellationToken = default)
    {
        RequireOwner();
        var now = DateTime.UtcNow;
        var invites = await _db.Invitations
            .Include(item => item.Organization)
            .Where(item => item.OrganizationId == _currentUser.OrganizationId
                           && item.AcceptedAt == null
                           && item.RevokedAt == null
                           && item.ExpiresAt > now)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        return invites.Select(ToPending).ToList();
    }

    public async Task<IReadOnlyList<PendingInvitation>> ListInvitesForCurrentEmailAsync(CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new InvalidOperationException("Sign in to continue.");
        }

        var email = NormalizeEmail(_currentUser.Email);
        var now = DateTime.UtcNow;
        var invites = await _db.Invitations
            .IgnoreQueryFilters()
            .Include(item => item.Organization)
            .Where(item => item.Email == email
                           && item.AcceptedAt == null
                           && item.RevokedAt == null
                           && item.ExpiresAt > now)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        return invites.Select(ToPending).ToList();
    }

    public async Task<BusinessMembership> AcceptInviteAsync(string token, CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new InvalidOperationException("Sign in to continue.");
        }

        var invite = await _db.Invitations
            .IgnoreQueryFilters()
            .Include(item => item.Organization)
            .FirstOrDefaultAsync(item => item.Token == token, cancellationToken);
        if (invite is null || invite.RevokedAt is not null || invite.AcceptedAt is not null || invite.ExpiresAt <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("This invite is no longer valid.");
        }

        if (!string.Equals(invite.Email, NormalizeEmail(_currentUser.Email), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This invite was sent to a different email address.");
        }

        var existing = await Memberships().AnyAsync(item => item.UserId == _currentUser.UserId, cancellationToken);
        if (existing)
        {
            throw new InvalidOperationException(
                "This sign-in already belongs to a business. One business per person for now.");
        }

        if (invite.Role != MembershipRole.Member)
        {
            throw new InvalidOperationException("Only a member invite can be accepted.");
        }

        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            OrganizationId = invite.OrganizationId,
            UserId = _currentUser.UserId,
            Role = MembershipRole.Member,
            CreatedAt = DateTime.UtcNow
        };
        invite.AcceptedAt = membership.CreatedAt;
        invite.AcceptedByUserId = _currentUser.UserId;
        _db.Memberships.Add(membership);
        await _db.SaveChangesAsync(cancellationToken);
        membership.Organization = invite.Organization;
        return ToMembership(membership);
    }

    private IQueryable<Membership> Memberships() =>
        _db.Memberships.IgnoreQueryFilters().Include(item => item.Organization);

    private void RequireMember()
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.HasOrganization)
        {
            throw new InvalidOperationException("Sign in to a business to continue.");
        }
    }

    private void RequireOwner()
    {
        RequireMember();
        if (!_currentUser.IsOwner)
        {
            throw new InvalidOperationException("Only an owner can do that.");
        }
    }

    private static BusinessMembership ToMembership(Membership membership) =>
        new(membership.UserId, membership.OrganizationId, membership.Organization.Name, membership.Role.ToString());

    private static PendingInvitation ToPending(Invitation invite) =>
        new(invite.Id, invite.OrganizationId, invite.Organization.Name, invite.Email, invite.Token, invite.ExpiresAt);

    internal static string NormalizeEmail(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
