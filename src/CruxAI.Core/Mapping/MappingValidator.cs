namespace CruxAI.Core.Mapping;

public static class MappingValidator
{
    public static IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string> mapping)
    {
        var errors = new List<string>();
        var assigned = mapping
            .Where(pair => !string.Equals(pair.Value, TransactionFields.Ignore, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var duplicates = assigned
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        foreach (var field in duplicates)
        {
            errors.Add($"More than one column is mapped to {field}. Keep a single source column per field.");
        }

        var fields = assigned.Select(pair => pair.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!fields.Contains(TransactionFields.Date))
        {
            errors.Add("Map a Date column.");
        }

        if (!fields.Contains(TransactionFields.Description))
        {
            errors.Add("Map a Description column.");
        }

        var hasAmount = fields.Contains(TransactionFields.Amount);
        var hasDebit = fields.Contains(TransactionFields.Debit);
        var hasCredit = fields.Contains(TransactionFields.Credit);

        if (!hasAmount && !(hasDebit || hasCredit))
        {
            errors.Add("Map an Amount column, or Debit and/or Credit columns.");
        }

        if (hasAmount && (hasDebit || hasCredit))
        {
            errors.Add("Use either Amount or Debit/Credit, not both.");
        }

        return errors;
    }

    public static string? HeaderFor(IReadOnlyDictionary<string, string> mapping, string field) =>
        mapping.FirstOrDefault(pair => string.Equals(pair.Value, field, StringComparison.OrdinalIgnoreCase)).Key;
}
