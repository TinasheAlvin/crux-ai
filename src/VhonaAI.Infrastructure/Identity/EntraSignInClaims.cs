using System.Security.Claims;

namespace VhonaAI.Infrastructure.Identity;

public readonly record struct ExternalSignInIdentity(string? ExternalId, string? Email, string? DisplayName);

/// <summary>
/// Reads the Entra External ID principal and stamps Vhona org claims onto it.
/// Demo cookie auth writes the same vhona_user_id / org_id claims directly.
/// </summary>
public static class EntraSignInClaims
{
    public static ExternalSignInIdentity Read(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var externalId = First(principal,
            "oid",
            "http://schemas.microsoft.com/identity/claims/objectidentifier");
        var email = FirstEmail(principal,
            ClaimTypes.Email,
            "email",
            "emails",
            "preferred_username",
            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress");
        var displayName = First(principal, "name", ClaimTypes.Name, ClaimTypes.GivenName);
        return new ExternalSignInIdentity(externalId, email, displayName);
    }

    public static void ApplyTenant(
        ClaimsIdentity identity,
        Guid userId,
        string email,
        string displayName,
        Guid organizationId,
        string organizationName,
        string role)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Replace(identity, "vhona_user_id", userId.ToString());
        Replace(identity, "org_id", organizationId.ToString());
        Replace(identity, "org_name", organizationName);
        Replace(identity, "org_role", role);
        Replace(identity, ClaimTypes.Email, email);
        Replace(identity, ClaimTypes.Name, displayName);
    }

    private static string? FirstEmail(ClaimsPrincipal principal, params string[] types)
    {
        foreach (var type in types)
        {
            var email = NormalizeEmail(principal.FindFirst(type)?.Value);
            if (email is not null)
            {
                return email;
            }
        }

        return null;
    }

    private static string? First(ClaimsPrincipal principal, params string[] types)
    {
        foreach (var type in types)
        {
            var value = principal.FindFirst(type)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    internal static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('['))
        {
            trimmed = trimmed.Trim().TrimStart('[').TrimEnd(']').Trim();
            var comma = trimmed.IndexOf(',');
            if (comma >= 0)
            {
                trimmed = trimmed[..comma];
            }

            trimmed = trimmed.Trim().Trim('"');
        }

        return trimmed.Contains('@') ? trimmed : null;
    }

    private static void Replace(ClaimsIdentity identity, string type, string value)
    {
        var existing = identity.FindAll(type).ToList();
        foreach (var claim in existing)
        {
            identity.RemoveClaim(claim);
        }

        identity.AddClaim(new Claim(type, value));
    }
}
