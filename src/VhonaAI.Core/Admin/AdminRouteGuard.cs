namespace VhonaAI.Core.Admin;

/// <summary>
/// Every /admin route uses this. A signed-in person who is not a Vhona admin gets the same
/// answer as a missing page. Anonymous visitors are sent to sign in.
/// </summary>
public static class AdminRouteGuard
{
    public const string ClaimType = "vhona_admin";
    public const string ClaimValue = "true";

    public static AdminRouteDecision Decide(bool isAuthenticated, bool isVhonaAdmin)
    {
        if (!isAuthenticated)
        {
            return AdminRouteDecision.Challenge;
        }

        if (!isVhonaAdmin)
        {
            return AdminRouteDecision.NotFound;
        }

        return AdminRouteDecision.Allow;
    }
}

public enum AdminRouteDecision
{
    Allow = 0,
    Challenge = 1,
    NotFound = 2
}
