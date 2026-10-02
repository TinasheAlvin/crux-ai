using System.Security.Claims;
using VhonaAI.Infrastructure;
using VhonaAI.Web.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace VhonaAI.Web.Identity;

public static class DemoAuthEndpoints
{
    public static IEndpointRouteBuilder MapDemoAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/demo-login", async (
            HttpContext http,
            OrgBootstrapper bootstrapper,
            IOptions<DemoAuthOptions> options) =>
        {
            var demo = options.Value;
            var (user, organization, membership) = await bootstrapper.EnsureDemoTenantAsync(
                demo.Email,
                demo.DisplayName,
                demo.OrganizationName);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new("vhona_user_id", user.Id.ToString()),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Name, user.DisplayName),
                new("org_id", organization.Id.ToString()),
                new("org_name", organization.Name),
                new("org_role", membership.Role.ToString())
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
                });

            return Results.Redirect("/");
        }).AllowAnonymous();

        return endpoints;
    }
}
