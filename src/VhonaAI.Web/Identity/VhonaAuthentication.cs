using System.Security.Claims;
using VhonaAI.Infrastructure;
using VhonaAI.Infrastructure.Hosting;
using VhonaAI.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;

namespace VhonaAI.Web.Identity;

public static class VhonaAuthentication
{
    public static void AddVhonaAuthentication(this WebApplicationBuilder builder)
    {
        if (HostingConfiguration.IsEntra(builder.Configuration))
        {
            HostingConfiguration.EnsureEntraConfigured(builder.Configuration);
            builder.Services
                .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("Auth:EntraExternalId"));

            builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.SaveTokens = false;
                if (!options.Scope.Contains("email"))
                {
                    options.Scope.Add("email");
                }

                var previousValidated = options.Events.OnTokenValidated;
                options.Events.OnTokenValidated = async context =>
                {
                    if (previousValidated is not null)
                    {
                        await previousValidated(context);
                    }

                    if (context.Result is not null && !context.Result.Succeeded)
                    {
                        return;
                    }

                    await ProvisionTenantAsync(context);
                };

                options.Events.OnRemoteFailure = context =>
                {
                    var message = context.Failure?.Message ?? "Sign-in failed.";
                    if (message.Length > 300)
                    {
                        message = message[..300];
                    }

                    context.HandleResponse();
                    context.Response.Redirect("/login?error=" + Uri.EscapeDataString(message));
                    return Task.CompletedTask;
                };
            });
        }
        else
        {
            builder.Services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie();
        }

        builder.Services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.Name = "vhonaai.auth";
            options.LoginPath = "/login";
            options.LogoutPath = "/auth/logout";
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromDays(7);
        });
    }

    private static async Task ProvisionTenantAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal?.Identity is not ClaimsIdentity identity)
        {
            context.Fail("Entra sign-in returned no principal.");
            return;
        }

        var signIn = EntraSignInClaims.Read(principal);
        if (string.IsNullOrWhiteSpace(signIn.ExternalId))
        {
            context.Fail("Entra sign-in did not include an object id (oid).");
            return;
        }

        if (string.IsNullOrWhiteSpace(signIn.Email))
        {
            context.Fail("Entra sign-in did not include an email claim. Add the email optional claim on the app registration.");
            return;
        }

        var bootstrapper = context.HttpContext.RequestServices.GetRequiredService<OrgBootstrapper>();
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var displayName = string.IsNullOrWhiteSpace(signIn.DisplayName) ? signIn.Email : signIn.DisplayName;
        var (user, organization, membership) = await bootstrapper.EnsureExternalUserAsync(
            signIn.ExternalId,
            signIn.Email,
            displayName,
            HostingConfiguration.DefaultOrganizationName(configuration),
            context.HttpContext.RequestAborted);

        EntraSignInClaims.ApplyTenant(
            identity,
            user.Id,
            user.Email,
            user.DisplayName,
            organization.Id,
            organization.Name,
            membership.Role.ToString());
    }
}
