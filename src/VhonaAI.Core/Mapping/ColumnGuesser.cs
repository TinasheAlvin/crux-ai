namespace VhonaAI.Core.Mapping;

public static class ColumnGuesser
{
    private static readonly (string Field, string[] Aliases)[] Catalog =
    [
        (TransactionFields.Date, [
            "date", "txn date", "txn_date", "transaction date", "trans date",
            "posted", "posted date", "value date", "datum", "trandate"
        ]),
        (TransactionFields.Description, [
            "description", "desc", "narrative", "details", "detail", "memo",
            "particulars", "description 1", "transaction description", "payee details"
        ]),
        (TransactionFields.Amount, [
            "amount", "zar amount", "amt", "value", "zar", "amount zar",
            "transaction amount", "nett", "net"
        ]),
        (TransactionFields.Debit, ["debit", "debits", "withdrawal", "money out", "paid out", "dr"]),
        (TransactionFields.Credit, ["credit", "credits", "deposit", "money in", "paid in", "cr"]),
        (TransactionFields.Category, ["category", "type", "class", "expense type", "account", "gl"]),
        (TransactionFields.Reference, [
            "reference", "ref", "ref no", "ref.", "invoice", "invoice no",
            "receipt", "receipt no", "document"
        ]),
        (TransactionFields.Counterparty, [
            "client", "customer", "payee", "beneficiary", "name", "counterparty",
            "supplier", "vendor", "contact"
        ]),
        (TransactionFields.Balance, [
            "balance", "running balance", "closing balance", "available balance",
            "acc balance", "account balance", "zar balance"
        ])
    ];

    public static IReadOnlyDictionary<string, string> Guess(IEnumerable<string> headers)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            var field = GuessOne(header);
            if (field is null || !usedFields.Add(field))
            {
                mapping[header] = TransactionFields.Ignore;
                continue;
            }

            mapping[header] = field;
        }

        return mapping;
    }

    public static IReadOnlyDictionary<string, string> MergeSaved(
        IEnumerable<string> headers,
        IReadOnlyDictionary<string, string> guessed,
        IReadOnlyDictionary<string, string>? saved)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            if (saved is not null
                && saved.TryGetValue(header, out var savedField)
                && TransactionFields.All.Contains(savedField, StringComparer.OrdinalIgnoreCase))
            {
                merged[header] = savedField;
            }
            else if (guessed.TryGetValue(header, out var guessedField))
            {
                merged[header] = guessedField;
            }
            else
            {
                merged[header] = TransactionFields.Ignore;
            }
        }

        return merged;
    }

    private static string? GuessOne(string header)
    {
        var normalized = Normalize(header);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        foreach (var (field, aliases) in Catalog)
        {
            if (aliases.Any(alias => Normalize(alias) == normalized))
            {
                return field;
            }
        }

        foreach (var (field, aliases) in Catalog)
        {
            if (aliases.Any(alias => normalized.Contains(Normalize(alias)) && Normalize(alias).Length >= 4))
            {
                return field;
            }
        }

        return null;
    }

    internal static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant()
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
