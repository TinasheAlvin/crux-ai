using System.Security.Claims;
using VhonaAI.Core.Identity;
using Microsoft.AspNetCore.Http;

namespace VhonaAI.Web.Identity;

public sealed class ClaimsCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClaimsCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal User => _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    public Guid UserId => Guid.Parse(RequireFirst("vhona_user_id", ClaimTypes.NameIdentifier));

    public Guid OrganizationId => Guid.Parse(Require("org_id"));

    public string Email => Require(ClaimTypes.Email);

    public string DisplayName => Require(ClaimTypes.Name);

    public string OrganizationName => Require("org_name");

    private string Require(string type) =>
        User.FindFirstValue(type) ?? string.Empty;

    private string RequireFirst(params string[] types)
    {
        foreach (var type in types)
        {
            var value = User.FindFirstValue(type);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
