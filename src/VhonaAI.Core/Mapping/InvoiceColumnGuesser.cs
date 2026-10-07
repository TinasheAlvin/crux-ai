namespace VhonaAI.Core.Mapping;

public static class InvoiceColumnGuesser
{
    private static readonly (string Field, string[] Aliases)[] Catalog =
    [
        (InvoiceFields.CustomerSourceId, ["customer id", "customer source id", "contact id", "debtor id", "account number"]),
        (InvoiceFields.CustomerName, ["customer", "customer name", "client", "client name", "debtor", "debtor name", "name"]),
        (InvoiceFields.ContactPerson, ["contact", "contact person", "attention", "contact name"]),
        (InvoiceFields.Phone, ["phone", "mobile", "whatsapp", "cell", "telephone", "tel"]),
        (InvoiceFields.Email, ["email", "e-mail", "email address"]),
        (InvoiceFields.CreditNoteNumber, ["credit note", "credit note no", "credit note number", "cn number"]),
        (InvoiceFields.InvoiceNumber, ["invoice", "invoice no", "invoice number", "inv no", "document no", "document number", "number"]),
        (InvoiceFields.BookedDate, ["booked date", "booked", "date booked", "posting date"]),
        (InvoiceFields.DueDate, ["due date", "due", "payment due", "date due"]),
        (InvoiceFields.PaidDate, ["paid date", "date paid", "payment date", "settled date"]),
        (InvoiceFields.InvoiceDate, ["invoice date", "date", "issue date", "document date", "tax date", "txn date"]),
        (InvoiceFields.AmountDue, ["amount due", "balance due", "outstanding", "amount outstanding", "balance outstanding", "open balance"]),
        (InvoiceFields.PaymentAmount, ["payment amount", "amount paid", "paid amount", "receipt amount"]),
        (InvoiceFields.CreditAmount, ["credit amount", "credit"]),
        (InvoiceFields.LineAmount, ["line amount", "line total", "item amount"]),
        (InvoiceFields.Amount, ["amount", "total", "invoice total", "invoice amount", "exclusive", "inclusive", "nett", "net"]),
        (InvoiceFields.Status, ["status", "invoice status"]),
        (InvoiceFields.Currency, ["currency", "curr"]),
        (InvoiceFields.Terms, ["terms", "payment terms"]),
        (InvoiceFields.ItemCode, ["item code", "sku", "item", "product code"]),
        (InvoiceFields.LineDescription, ["line description", "item description", "description", "details", "narrative"]),
        (InvoiceFields.Quantity, ["quantity", "qty"]),
        (InvoiceFields.RowType, ["row type", "type", "record type"])
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
                mapping[header] = InvoiceFields.Ignore;
                continue;
            }

            mapping[header] = field;
        }

        return mapping;
    }

    private static string? GuessOne(string header)
    {
        var normalized = ColumnGuesser.Normalize(header);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        foreach (var (field, aliases) in Catalog)
        {
            if (aliases.Any(alias => ColumnGuesser.Normalize(alias) == normalized))
            {
                return field;
            }
        }

        var contains = Catalog
            .SelectMany(entry => entry.Aliases.Select(alias => (entry.Field, Alias: ColumnGuesser.Normalize(alias))))
            .Where(pair => pair.Alias.Length >= 4)
            .OrderByDescending(pair => pair.Alias.Length);

        foreach (var (field, alias) in contains)
        {
            if (normalized.Contains(alias, StringComparison.Ordinal))
            {
                return field;
            }
        }

        return null;
    }
}
