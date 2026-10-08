namespace VhonaAI.Core.Admin;

/// <summary>
/// A disabled sign-in cannot use the product. A disabled business cannot be used, including by a Vhona admin.
/// The admin console stays available so that admin can re-enable the business.
/// </summary>
public static class ProductAccessGate
{
    public static ProductAccess Decide(bool isVhonaAdmin, bool userDisabled, bool organizationDisabled, string? path)
    {
        if (IsAlwaysAllowed(path))
        {
            return ProductAccess.Continue;
        }

        if (userDisabled)
        {
            return ProductAccess.AccountDisabled;
        }

        if (isVhonaAdmin && IsAdminPath(path))
        {
            return ProductAccess.Continue;
        }

        if (organizationDisabled && !IsAdminPath(path))
        {
            return ProductAccess.BusinessDisabled;
        }

        return ProductAccess.Continue;
    }

    public static bool SkipsAccountCheck(string? path) => IsAlwaysAllowed(path);

    public static bool IsAdminPath(string? path) =>
        (path ?? string.Empty).StartsWith("/admin", StringComparison.OrdinalIgnoreCase);

    private static bool IsAlwaysAllowed(string? path)
    {
        var value = path ?? string.Empty;
        return value.StartsWith("/account-disabled", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/business-disabled", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/auth", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/healthz", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/signin-oidc", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/signout-callback-oidc", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/_content", StringComparison.OrdinalIgnoreCase)
            || Path.HasExtension(value);
    }
}

public enum ProductAccess
{
    Continue = 0,
    AccountDisabled = 1,
    BusinessDisabled = 2
}
