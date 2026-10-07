namespace VhonaAI.Core.Calling;

/// <summary>
/// Hand-off links only. The owner presses send in WhatsApp or their mail app.
/// These methods do not open a network connection.
/// </summary>
public static class ReminderHandoff
{
    public static string? WhatsAppLink(string? phone, string body)
    {
        var digits = InternationalDigits(phone);
        if (digits is null || string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        return "https://wa.me/" + digits + "?text=" + Uri.EscapeDataString(body);
    }

    public static string? EmailLink(string? email, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return null;
        }

        return "mailto:" + email.Trim()
            + "?subject=" + Uri.EscapeDataString(subject)
            + "&body=" + Uri.EscapeDataString(body);
    }

    public static string? InternationalDigits(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length is < 8 or > 15)
        {
            return null;
        }

        if (digits.StartsWith('0') && digits.Length == 10)
        {
            digits = "27" + digits[1..];
        }

        return digits;
    }
}
