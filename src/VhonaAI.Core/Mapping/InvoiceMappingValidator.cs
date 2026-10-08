namespace VhonaAI.Core.Mapping;

public static class InvoiceMappingValidator
{
    public static IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string> mapping)
    {
        var errors = new List<string>();
        var assigned = mapping
            .Where(pair => !string.Equals(pair.Value, InvoiceFields.Ignore, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var field in assigned.Select(pair => pair.Value))
        {
            if (!InvoiceFields.All.Contains(field, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{field} is not an invoice field.");
            }
        }

        var duplicates = assigned
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var field in duplicates)
        {
            errors.Add($"More than one column is mapped to {field}. Keep a single source column per field.");
        }

        var fields = assigned.Select(pair => pair.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!fields.Contains(InvoiceFields.CustomerName))
        {
            errors.Add("Map a CustomerName column.");
        }

        if (!fields.Contains(InvoiceFields.InvoiceNumber))
        {
            errors.Add("Map an InvoiceNumber column.");
        }

        if (!fields.Contains(InvoiceFields.InvoiceDate))
        {
            errors.Add("Map an InvoiceDate column.");
        }

        if (!fields.Contains(InvoiceFields.Amount))
        {
            errors.Add("Map an Amount column.");
        }

        return errors;
    }
}
