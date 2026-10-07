using System.Security.Claims;
using VhonaAI.Application.Business;

namespace VhonaAI.Web.Identity;

public static class BusinessAuthEndpoints
{
    public static IEndpointRouteBuilder MapBusinessAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/create-business", async (
            HttpContext http,
            IBusinessAppService businesses) =>
        {
            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            var name = form["name"].ToString();
            if (!Guid.TryParse(http.User.FindFirstValue("vhona_user_id"), out var userId))
            {
                return Results.Redirect("/login?error=" + Uri.EscapeDataString("Sign in before naming a business."));
            }

            try
            {
                await businesses.CreateBusinessAsync(userId, name, http.RequestAborted);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Redirect("/onboarding?error=" + Uri.EscapeDataString(ex.Message));
            }

            return Results.Redirect("/");
        }).RequireAuthorization();

        endpoints.MapPost("/auth/accept-invite", async (
            HttpContext http,
            IBusinessAppService businesses) =>
        {
            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            var token = form["token"].ToString();
            try
            {
                await businesses.AcceptInviteAsync(token, http.RequestAborted);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Redirect("/onboarding?error=" + Uri.EscapeDataString(ex.Message));
            }

            return Results.Redirect("/");
        }).RequireAuthorization();

        return endpoints;
    }
}
