using CruxAI.Infrastructure.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace CruxAI.Web.Identity;

public static class CruxAuthEndpoints
{
    public static IEndpointRouteBuilder MapCruxAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var configuration = endpoints.ServiceProvider.GetRequiredService<IConfiguration>();
        if (HostingConfiguration.IsEntra(configuration))
        {
            endpoints.MapGet("/auth/signin", () => Results.Challenge(
                    new AuthenticationProperties { RedirectUri = "/" },
                    [OpenIdConnectDefaults.AuthenticationScheme]))
                .AllowAnonymous();
        }
        else
        {
            endpoints.MapDemoAuthEndpoints();
        }

        endpoints.MapPost("/auth/logout", (HttpContext http, IConfiguration config) =>
        {
            if (HostingConfiguration.IsEntra(config))
            {
                return Results.SignOut(
                    new AuthenticationProperties { RedirectUri = "/login" },
                    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
            }

            return Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/login" },
                [CookieAuthenticationDefaults.AuthenticationScheme]);
        });

        return endpoints;
    }
}
