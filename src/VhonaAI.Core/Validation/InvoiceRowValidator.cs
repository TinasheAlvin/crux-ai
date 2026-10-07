using VhonaAI.Core.Csv;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Mapping;
using VhonaAI.Core.Parsing;

namespace VhonaAI.Core.Validation;

public static class InvoiceRowValidator
{
    public static IReadOnlyList<ValidatedInvoiceRow> Validate(
        CsvTable table,
        IReadOnlyDictionary<string, string> mapping,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>>? corrections = null)
    {
        var headers = MapHeaders(mapping);
        var results = new List<ValidatedInvoiceRow>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            var rowCorrections = GetCorrections(corrections, row.SourceRowNumber);
            var fields = ReadFields(row, headers, rowCorrections);
            var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parsed = TryParse(row.SourceRowNumber, fields, errors);
            results.Add(new ValidatedInvoiceRow
            {
                SourceRowNumber = row.SourceRowNumber,
                Fields = fields,
                Errors = errors,
                Parsed = parsed
            });
        }

        FlagRepeatedInvoicesWithoutLines(results);
        return results;
    }

    private static ParsedInvoiceRow? TryParse(
        int sourceRowNumber,
        IReadOnlyDictionary<string, string> fields,
        Dictionary<string, string> errors)
    {
        var kind = ResolveKind(fields, errors);
        var customer = fields[InvoiceFields.CustomerName].Trim();
        var invoiceNumber = fields[InvoiceFields.InvoiceNumber].Trim();
        var invoiceDate = ParseOptionalDate(fields[InvoiceFields.InvoiceDate], InvoiceFields.InvoiceDate, errors);
        var bookedDate = ParseOptionalDate(fields[InvoiceFields.BookedDate], InvoiceFields.BookedDate, errors);
        var dueDate = ParseOptionalDate(fields[InvoiceFields.DueDate], InvoiceFields.DueDate, errors);
        var paidDate = ParseOptionalDate(fields[InvoiceFields.PaidDate], InvoiceFields.PaidDate, errors);
        var amount = ParseOptionalAmount(fields[InvoiceFields.Amount], InvoiceFields.Amount, errors);
        var amountDue = ParseOptionalAmount(fields[InvoiceFields.AmountDue], InvoiceFields.AmountDue, errors);
        var paymentAmount = ParseOptionalAmount(fields[InvoiceFields.PaymentAmount], InvoiceFields.PaymentAmount, errors);
        var creditAmount = ParseOptionalAmount(fields[InvoiceFields.CreditAmount], InvoiceFields.CreditAmount, errors);
        var lineAmount = ParseOptionalAmount(fields[InvoiceFields.LineAmount], InvoiceFields.LineAmount, errors);
        var quantity = ParseOptionalAmount(fields[InvoiceFields.Quantity], InvoiceFields.Quantity, errors);
        var email = EmptyToNull(fields[InvoiceFields.Email]);
        var currency = fields[InvoiceFields.Currency].Trim();
        var statusText = fields[InvoiceFields.Status];

        if (email is not null && !email.Contains('@'))
        {
            errors[InvoiceFields.Email] = "Email must contain @.";
        }

        if (currency.Length == 0)
        {
            currency = "ZAR";
        }
        else if (currency.Length != 3 || currency.Any(ch => !char.IsLetter(ch)))
        {
            errors[InvoiceFields.Currency] = "Currency must be a 3-letter code such as ZAR.";
        }
        else
        {
            currency = currency.ToUpperInvariant();
        }

        InvoiceStatus? status = null;
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            if (!InvoiceStatusParser.TryParse(statusText, out var parsedStatus))
            {
                errors[InvoiceFields.Status] = "Status must be draft, open, paid, void, or credited.";
            }
            else
            {
                status = parsedStatus;
            }
        }

        if (kind == ImportedLedgerRowKind.Invoice)
        {
            RequireText(customer, InvoiceFields.CustomerName, "Customer name is required.", errors);
            RequireText(invoiceNumber, InvoiceFields.InvoiceNumber, "Invoice number is required.", errors);
            if (string.IsNullOrWhiteSpace(fields[InvoiceFields.InvoiceDate]))
            {
                errors.TryAdd(InvoiceFields.InvoiceDate, "Invoice date is required.");
            }

            if (amount is null && !errors.ContainsKey(InvoiceFields.Amount))
            {
                errors[InvoiceFields.Amount] = "Amount is required.";
            }
            else if (amount is <= 0 && !errors.ContainsKey(InvoiceFields.Amount))
            {
                errors[InvoiceFields.Amount] = "Amount must be greater than zero.";
            }
        }
        else if (kind == ImportedLedgerRowKind.Payment)
        {
            var paid = paymentAmount ?? amount;
            if (paid is null)
            {
                errors[InvoiceFields.PaymentAmount] = "Payment amount is required.";
            }
            else if (paid <= 0)
            {
                errors[InvoiceFields.PaymentAmount] = "Payment amount must be greater than zero.";
            }

            if (paidDate is null && invoiceDate is null)
            {
                errors[InvoiceFields.PaidDate] = "Paid date is required.";
            }

            if (customer.Length == 0 && invoiceNumber.Length == 0)
            {
                errors[InvoiceFields.CustomerName] = "A payment needs a customer or an invoice number.";
            }
        }
        else
        {
            var credit = creditAmount ?? amount;
            if (credit is null)
            {
                errors[InvoiceFields.CreditAmount] = "Credit amount is required.";
            }
            else if (credit == 0)
            {
                errors[InvoiceFields.CreditAmount] = "Credit amount must not be zero.";
            }

            if (invoiceDate is null && paidDate is null)
            {
                errors[InvoiceFields.InvoiceDate] = "Credit note date is required.";
            }

            if (customer.Length == 0 && invoiceNumber.Length == 0 && string.IsNullOrWhiteSpace(fields[InvoiceFields.CreditNoteNumber]))
            {
                errors[InvoiceFields.CustomerName] = "A credit note needs a customer, an invoice number, or a credit note number.";
            }
        }

        if (errors.Count > 0)
        {
            return null;
        }

        if (kind == ImportedLedgerRowKind.Invoice && amount is not null)
        {
            status ??= paidDate is not null || amountDue == 0 ? InvoiceStatus.Paid : InvoiceStatus.Open;
            amountDue ??= status is InvoiceStatus.Paid or InvoiceStatus.Void or InvoiceStatus.Credited ? 0 : amount;
        }

        var lineDescription = EmptyToNull(fields[InvoiceFields.LineDescription]);
        var itemCode = EmptyToNull(fields[InvoiceFields.ItemCode]);
        return new ParsedInvoiceRow
        {
            SourceRowNumber = sourceRowNumber,
            Kind = kind,
            CustomerName = customer,
            ContactPerson = EmptyToNull(fields[InvoiceFields.ContactPerson]),
            Phone = EmptyToNull(fields[InvoiceFields.Phone]),
            Email = email,
            CustomerSourceId = EmptyToNull(fields[InvoiceFields.CustomerSourceId]),
            InvoiceNumber = EmptyToNull(invoiceNumber),
            InvoiceDate = invoiceDate,
            BookedDate = bookedDate,
            DueDate = dueDate,
            PaidDate = paidDate,
            Amount = amount,
            AmountDue = amountDue,
            Status = status,
            Currency = currency,
            Terms = EmptyToNull(fields[InvoiceFields.Terms]),
            LineDescription = lineDescription,
            ItemCode = itemCode,
            LineAmount = lineAmount,
            Quantity = quantity,
            PaymentAmount = paymentAmount,
            CreditAmount = creditAmount,
            CreditNoteNumber = EmptyToNull(fields[InvoiceFields.CreditNoteNumber]),
            HasLineDetail = lineDescription is not null || itemCode is not null || lineAmount is not null
        };
    }

    private static void FlagRepeatedInvoicesWithoutLines(List<ValidatedInvoiceRow> results)
    {
        var groups = results
            .Where(row => row.Parsed is { Kind: ImportedLedgerRowKind.Invoice, InvoiceNumber: not null })
            .GroupBy(row => row.Parsed!.InvoiceNumber!, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var rows = group.ToList();
            if (rows.Count < 2 || rows.Any(row => row.Parsed!.HasLineDetail))
            {
                continue;
            }

            foreach (var row in rows.Skip(1))
            {
                var errors = new Dictionary<string, string>(row.Errors, StringComparer.OrdinalIgnoreCase)
                {
                    [InvoiceFields.InvoiceNumber] =
                        "This invoice number is repeated. Map a line description or line amount so the extra rows become lines."
                };
                var index = results.FindIndex(item => item.SourceRowNumber == row.SourceRowNumber);
                results[index] = new ValidatedInvoiceRow
                {
                    SourceRowNumber = row.SourceRowNumber,
                    Fields = row.Fields,
                    Errors = errors,
                    Parsed = null
                };
            }
        }
    }

    private static ImportedLedgerRowKind ResolveKind(
        IReadOnlyDictionary<string, string> fields,
        Dictionary<string, string> errors)
    {
        var raw = fields[InvoiceFields.RowType];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ImportedLedgerRowKind.Invoice;
        }

        if (!LedgerRowKindParser.TryParse(raw, out var kind))
        {
            errors[InvoiceFields.RowType] = "Row type must be invoice, payment, or credit note.";
            return ImportedLedgerRowKind.Invoice;
        }

        return kind;
    }

    private static DateOnly? ParseOptionalDate(string raw, string field, Dictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!ValueParsers.TryParseDate(raw, out var date))
        {
            errors[field] = "Use a date such as 2026-03-16 or 16/03/2026.";
            return null;
        }

        return date;
    }

    private static decimal? ParseOptionalAmount(string raw, string field, Dictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!ValueParsers.TryParseAmount(raw, out var amount))
        {
            errors[field] = "Amount must be a number, e.g. 12400.00 or R 1,250.00.";
            return null;
        }

        return amount;
    }

    private static void RequireText(string value, string field, string message, Dictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = message;
        }
    }

    private static Dictionary<string, string> MapHeaders(IReadOnlyDictionary<string, string> mapping)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in InvoiceFields.All)
        {
            if (field == InvoiceFields.Ignore)
            {
                continue;
            }

            var header = mapping.FirstOrDefault(pair =>
                string.Equals(pair.Value, field, StringComparison.OrdinalIgnoreCase)).Key;
            if (!string.IsNullOrEmpty(header))
            {
                headers[field] = header;
            }
        }

        return headers;
    }

    private static Dictionary<string, string> ReadFields(
        CsvRow row,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyDictionary<string, string> corrections)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in InvoiceFields.All)
        {
            if (field == InvoiceFields.Ignore)
            {
                continue;
            }

            if (corrections.TryGetValue(field, out var corrected) && corrected is not null)
            {
                fields[field] = corrected;
                continue;
            }

            if (headers.TryGetValue(field, out var header)
                && row.Values.TryGetValue(header, out var value))
            {
                fields[field] = value;
                continue;
            }

            fields[field] = string.Empty;
        }

        return fields;
    }

    private static IReadOnlyDictionary<string, string> GetCorrections(
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>>? corrections,
        int rowNumber)
    {
        if (corrections is not null && corrections.TryGetValue(rowNumber, out var row) && row is not null)
        {
            return row;
        }

        return new Dictionary<string, string>();
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ValidatedInvoiceRow
{
    public required int SourceRowNumber { get; init; }
    public required IReadOnlyDictionary<string, string> Fields { get; init; }
    public required IReadOnlyDictionary<string, string> Errors { get; init; }
    public ParsedInvoiceRow? Parsed { get; init; }
    public bool IsValid => Errors.Count == 0 && Parsed is not null;
}

public sealed class ParsedInvoiceRow
{
    public required int SourceRowNumber { get; init; }
    public required ImportedLedgerRowKind Kind { get; init; }
    public required string CustomerName { get; init; }
    public string? ContactPerson { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? CustomerSourceId { get; init; }
    public string? InvoiceNumber { get; init; }
    public DateOnly? InvoiceDate { get; init; }
    public DateOnly? BookedDate { get; init; }
    public DateOnly? DueDate { get; init; }
    public DateOnly? PaidDate { get; init; }
    public decimal? Amount { get; init; }
    public decimal? AmountDue { get; init; }
    public InvoiceStatus? Status { get; init; }
    public string Currency { get; init; } = "ZAR";
    public string? Terms { get; init; }
    public string? LineDescription { get; init; }
    public string? ItemCode { get; init; }
    public decimal? LineAmount { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? PaymentAmount { get; init; }
    public decimal? CreditAmount { get; init; }
    public string? CreditNoteNumber { get; init; }
    public bool HasLineDetail { get; init; }
}
