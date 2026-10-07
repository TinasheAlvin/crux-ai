namespace VhonaAI.Web.Identity;

public static class LocalReturnUrl
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith('/') || trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.Contains('\\'))
        {
            return null;
        }

        return trimmed;
    }
}
