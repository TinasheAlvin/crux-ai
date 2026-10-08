using System.Security.Claims;
using VhonaAI.Core.Admin;
using VhonaAI.Core.Identity;
using Microsoft.AspNetCore.Http;

namespace VhonaAI.Web.Identity;

public sealed class ClaimsVhonaAdmin : IVhonaAdmin
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClaimsVhonaAdmin(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal User => _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAdmin => User.HasClaim(AdminRouteGuard.ClaimType, AdminRouteGuard.ClaimValue);

    public Guid UserId =>
        Guid.TryParse(User.FindFirstValue("vhona_user_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : Guid.Empty;

    public string Email => User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

    public string? ObjectId =>
        User.FindFirstValue("oid")
        ?? User.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");
}
