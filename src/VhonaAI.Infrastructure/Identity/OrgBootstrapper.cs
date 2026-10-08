using VhonaAI.Core.Entities;
using VhonaAI.Core.Identity;
using VhonaAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Infrastructure;

public sealed class OrgBootstrapper
{
    private readonly VhonaDbContext _db;

    public OrgBootstrapper(VhonaDbContext db)
    {
        _db = db;
    }

    public async Task<(AppUser User, Organization Organization, Membership Membership)> EnsureDemoTenantAsync(
        string email,
        string displayName,
        string organizationName,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                ExternalId = $"demo:{email}",
                Email = email,
                DisplayName = displayName,
                CreatedAt = DateTime.UtcNow
            };
            _db.Users.Add(user);
        }
        else
        {
            user.DisplayName = displayName;
        }

        var (organization, membership) = await EnsureOwnerMembershipAsync(user, organizationName, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return (user, organization, membership);
    }

    /// <summary>
    /// Signs in a demo person the way a first Entra visit does: an account, and no business yet.
    /// If that email already owns a business, a fresh account is returned so onboarding stays reachable.
    /// </summary>
    public async Task<AppUser> EnsureDemoUserWithoutBusinessAsync(
        string email,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        email = (email ?? string.Empty).Trim();
        var user = await UpsertExternalUserAsync($"demo:{email}", email, displayName, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (await FindMembershipAsync(user.Id, cancellationToken) is null)
        {
            return user;
        }

        var at = email.IndexOf('@');
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var freshEmail = at > 0
            ? $"{email[..at]}+{stamp}{email[at..]}"
            : $"{stamp}@harbourstreet.local";
        var fresh = await UpsertExternalUserAsync($"demo:{freshEmail}", freshEmail, displayName, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return fresh;
    }

    public async Task<(AppUser User, Organization Organization, Membership Membership)> EnsureExternalUserAsync(
        string externalId,
        string email,
        string displayName,
        string organizationName,
        CancellationToken cancellationToken = default)
    {
        externalId = TrimTo((externalId ?? string.Empty).Trim(), 128);
        email = TrimTo((email ?? string.Empty).Trim(), 320);
        displayName = (displayName ?? string.Empty).Trim();
        organizationName = (organizationName ?? string.Empty).Trim();

        if (externalId.Length == 0)
        {
            throw new InvalidOperationException("Entra sign-in did not include an object id (oid).");
        }

        if (email.Length == 0 || !email.Contains('@'))
        {
            throw new InvalidOperationException(
                "Entra sign-in did not include an email claim. Add the email optional claim on the app registration.");
        }

        if (displayName.Length == 0)
        {
            displayName = email;
        }

        displayName = TrimTo(displayName, 200);
        organizationName = TrimTo(organizationName, 200);

        var user = await UpsertExternalUserAsync(externalId, email, displayName, cancellationToken);
        var (organization, membership) = await EnsureOwnerMembershipAsync(user, organizationName, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return (user, organization, membership);
    }

    private async Task<(Organization Organization, Membership Membership)> EnsureOwnerMembershipAsync(
        AppUser user,
        string organizationName,
        CancellationToken cancellationToken)
    {
        var membership = await _db.Memberships
            .IgnoreQueryFilters()
            .Include(m => m.Organization)
            .FirstOrDefaultAsync(m => m.UserId == user.Id, cancellationToken);

        Organization organization;
        if (membership is null)
        {
            var businessName = BusinessNameRules.Normalize(organizationName);
            organization = new Organization
            {
                Id = Guid.NewGuid(),
                Name = businessName,
                CreatedAt = DateTime.UtcNow
            };
            membership = new Membership
            {
                Id = Guid.NewGuid(),
                OrganizationId = organization.Id,
                UserId = user.Id,
                Role = MembershipRole.Owner,
                CreatedAt = DateTime.UtcNow
            };
            _db.Organizations.Add(organization);
            _db.Memberships.Add(membership);
        }
        else
        {
            organization = membership.Organization;
        }

        return (organization, membership);
    }

    /// <summary>
    /// Creates or updates the person from an Entra sign-in. Does not create or rename a business.
    /// </summary>
    public async Task<AppUser> UpsertExternalUserAsync(
        string externalId,
        string email,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        externalId = TrimTo((externalId ?? string.Empty).Trim(), 128);
        email = TrimTo((email ?? string.Empty).Trim(), 320);
        displayName = (displayName ?? string.Empty).Trim();
        if (externalId.Length == 0)
        {
            throw new InvalidOperationException("Entra sign-in did not include an object id (oid).");
        }

        if (email.Length == 0 || !email.Contains('@'))
        {
            throw new InvalidOperationException(
                "Entra sign-in did not include an email claim. Add the email optional claim on the app registration.");
        }

        if (displayName.Length == 0)
        {
            displayName = email;
        }

        displayName = TrimTo(displayName, 200);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId, cancellationToken);
        if (user is null)
        {
            var emailTaken = await _db.Users.AnyAsync(u => u.Email == email, cancellationToken);
            if (emailTaken)
            {
                throw new InvalidOperationException(
                    "An account with this email already exists under a different sign-in id. Use the original sign-in for this demo user.");
            }

            user = new AppUser
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                Email = email,
                DisplayName = displayName,
                CreatedAt = DateTime.UtcNow
            };
            _db.Users.Add(user);
        }
        else
        {
            var emailTaken = await _db.Users.AnyAsync(
                u => u.Email == email && u.Id != user.Id,
                cancellationToken);
            if (emailTaken)
            {
                throw new InvalidOperationException(
                    "Another account already uses this email. Keep the original email on the Entra user for this demo.");
            }

            user.Email = email;
            user.DisplayName = displayName;
        }

        return user;
    }

    public Task<Membership?> FindMembershipAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _db.Memberships
            .IgnoreQueryFilters()
            .Include(membership => membership.Organization)
            .FirstOrDefaultAsync(membership => membership.UserId == userId, cancellationToken);

    private static string TrimTo(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
