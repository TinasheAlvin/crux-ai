namespace VhonaAI.Core.Identity;

public static class BusinessNameRules
{
    public const int MaxLength = 200;

    public static string Normalize(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("Enter a business name.");
        }

        if (trimmed.Length > MaxLength)
        {
            throw new InvalidOperationException($"Business name must be {MaxLength} characters or fewer.");
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new InvalidOperationException("Business name cannot include line breaks.");
        }

        return trimmed;
    }
}
