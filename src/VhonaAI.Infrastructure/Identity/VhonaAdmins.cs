using Microsoft.Extensions.Configuration;

namespace VhonaAI.Infrastructure.Identity;

/// <summary>
/// Who may open the admin console. The list is configuration (app settings or a Key Vault reference),
/// never a role an owner can grant. <see cref="InternalAdmins.ConfigurationKey"/> is still accepted
/// so the earlier partner-events allow-list keeps working.
/// </summary>
public static class VhonaAdmins
{
    public const string EmailsKey = "Auth:VhonaAdminEmails";
    public const string ObjectIdsKey = "Auth:VhonaAdminObjectIds";

    public static bool IsMatch(IConfiguration configuration, string? email, string? objectId)
    {
        if (Listed(configuration[EmailsKey], email) || Listed(configuration[InternalAdmins.ConfigurationKey], email))
        {
            return true;
        }

        return Listed(configuration[ObjectIdsKey], objectId);
    }

    private static bool Listed(string? raw, string? value)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var target = value.Trim();
        foreach (var candidate in raw.Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(candidate, target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
