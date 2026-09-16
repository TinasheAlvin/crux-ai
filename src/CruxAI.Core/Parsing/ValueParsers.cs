using System.Globalization;
using System.Text.RegularExpressions;

namespace CruxAI.Core.Parsing;

public static class ValueParsers
{
    private static readonly CultureInfo[] DateCultures =
    [
        CultureInfo.GetCultureInfo("en-ZA"),
        CultureInfo.InvariantCulture,
        CultureInfo.GetCultureInfo("en-GB")
    ];

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy",
        "yyyy/MM/dd",
        "dd MMM yyyy",
        "d MMM yyyy",
        "yyyyMMdd"
    ];

    public static bool TryParseDate(string? raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();
        foreach (var culture in DateCultures)
        {
            if (DateOnly.TryParseExact(text, DateFormats, culture, DateTimeStyles.None, out date))
            {
                return true;
            }

            if (DateOnly.TryParse(text, culture, DateTimeStyles.None, out date))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryParseAmount(string? raw, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim();
        var negative = false;

        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            negative = true;
            text = text[1..^1].Trim();
        }

        text = Regex.Replace(text, @"^R\s*", "", RegexOptions.IgnoreCase);
        text = text.Replace("ZAR", "", StringComparison.OrdinalIgnoreCase).Trim();

        if (text.StartsWith('-'))
        {
            negative = true;
            text = text[1..].Trim();
        }
        else if (text.StartsWith('+'))
        {
            text = text[1..].Trim();
        }

        if (TryParseDecimal(text, out amount))
        {
            if (negative)
            {
                amount = -Math.Abs(amount);
            }

            return true;
        }

        return false;
    }

    public static decimal CombineDebitCredit(string? debitRaw, string? creditRaw)
    {
        var hasDebit = TryParseAmount(debitRaw, out var debit);
        var hasCredit = TryParseAmount(creditRaw, out var credit);

        if (!hasDebit && !hasCredit)
        {
            throw new FormatException("Neither debit nor credit could be parsed.");
        }

        var outValue = hasDebit ? -Math.Abs(debit) : 0;
        var inValue = hasCredit ? Math.Abs(credit) : 0;
        return outValue + inValue;
    }

    private static bool TryParseDecimal(string text, out decimal amount)
    {
        amount = 0;
        var compact = text.Trim();

        if (compact.Contains(',') && compact.Contains('.'))
        {
            var lastComma = compact.LastIndexOf(',');
            var lastDot = compact.LastIndexOf('.');
            if (lastComma > lastDot)
            {
                compact = compact.Replace(".", "").Replace(',', '.');
            }
            else
            {
                compact = compact.Replace(",", "");
            }
        }
        else if (compact.Contains(',') && !compact.Contains('.'))
        {
            var parts = compact.Split(',');
            compact = parts[^1].Length is 2 or 3
                ? compact.Replace(" ", "").Replace(',', '.')
                : compact.Replace(",", "");
        }

        compact = compact.Replace(" ", "");
        return decimal.TryParse(compact, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }
}
