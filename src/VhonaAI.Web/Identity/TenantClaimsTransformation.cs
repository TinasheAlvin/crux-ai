using System.Security.Claims;
using VhonaAI.Infrastructure;
using VhonaAI.Infrastructure.Data;
using VhonaAI.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Web.Identity;

/// <summary>
/// Refreshes business name and role from the database on each request,
/// so a rename or an accepted invite shows up without another sign-in.
/// </summary>
public sealed class TenantClaimsTransformation : IClaimsTransformation
{
    private readonly VhonaDbContext _db;
    private readonly IConfiguration _configuration;

    public TenantClaimsTransformation(VhonaDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || identity.IsAuthenticated != true)
        {
            return principal;
        }

        if (identity.HasClaim("vhona_tenant_bound", "1"))
        {
            return principal;
        }

        identity.AddClaim(new Claim("vhona_tenant_bound", "1"));

        if (!Guid.TryParse(identity.FindFirst("vhona_user_id")?.Value, out var userId))
        {
            return principal;
        }

        var membership = await _db.Memberships
            .IgnoreQueryFilters()
            .Include(item => item.Organization)
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => item.UserId == userId);

        var email = identity.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
        var displayName = identity.FindFirst(ClaimTypes.Name)?.Value ?? email;
        if (membership is null)
        {
            EntraSignInClaims.ApplyUser(identity, userId, email, displayName);
        }
        else
        {
            EntraSignInClaims.ApplyTenant(
                identity,
                userId,
                string.IsNullOrWhiteSpace(membership.User?.Email) ? email : membership.User.Email,
                string.IsNullOrWhiteSpace(membership.User?.DisplayName) ? displayName : membership.User.DisplayName,
                membership.OrganizationId,
                membership.Organization.Name,
                membership.Role.ToString());
        }

        if (InternalAdmins.IsAdmin(_configuration, identity.FindFirst(ClaimTypes.Email)?.Value))
        {
            if (!identity.HasClaim("internal_admin", "true"))
            {
                identity.AddClaim(new Claim("internal_admin", "true"));
            }
        }

        return principal;
    }
}
