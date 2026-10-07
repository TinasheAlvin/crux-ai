using Microsoft.Extensions.Configuration;

namespace VhonaAI.Infrastructure.Identity;

public static class InternalAdmins
{
    public const string ConfigurationKey = "Auth:InternalAdminEmails";

    public static bool IsAdmin(IConfiguration configuration, string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var raw = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var target = email.Trim();
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
