using CruxAI.Core.Csv;
using CruxAI.Core.Mapping;
using CruxAI.Core.Parsing;

namespace CruxAI.Core.Validation;

public static class ImportRowValidator
{
    public static IReadOnlyList<ValidatedImportRow> Validate(
        CsvTable table,
        IReadOnlyDictionary<string, string> mapping,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>>? corrections = null)
    {
        var dateHeader = MappingValidator.HeaderFor(mapping, TransactionFields.Date);
        var descriptionHeader = MappingValidator.HeaderFor(mapping, TransactionFields.Description);
        var amountHeader = MappingValidator.HeaderFor(mapping, TransactionFields.Amount);
        var debitHeader = MappingValidator.HeaderFor(mapping, TransactionFields.Debit);
        var creditHeader = MappingValidator.HeaderFor(mapping, TransactionFields.Credit);
        var categoryHeader = MappingValidator.HeaderFor(mapping, TransactionFields.Category);
        var referenceHeader = MappingValidator.HeaderFor(mapping, TransactionFields.Reference);
        var counterpartyHeader = MappingValidator.HeaderFor(mapping, TransactionFields.Counterparty);
        var balanceHeader = MappingValidator.HeaderFor(mapping, TransactionFields.Balance);

        var results = new List<ValidatedImportRow>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            var rowCorrections = GetCorrections(corrections, row.SourceRowNumber);
            var dateRaw = Resolve(row, dateHeader, TransactionFields.Date, rowCorrections);
            var descriptionRaw = Resolve(row, descriptionHeader, TransactionFields.Description, rowCorrections);
            var amountRaw = Resolve(row, amountHeader, TransactionFields.Amount, rowCorrections);
            var debitRaw = Resolve(row, debitHeader, TransactionFields.Debit, rowCorrections);
            var creditRaw = Resolve(row, creditHeader, TransactionFields.Credit, rowCorrections);
            var balanceRaw = Resolve(row, balanceHeader, TransactionFields.Balance, rowCorrections);

            var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            DateOnly? date = null;
            decimal? amount = null;

            if (!ValueParsers.TryParseDate(dateRaw, out var parsedDate))
            {
                errors[TransactionFields.Date] = "Use a date such as 2026-03-16 or 16/03/2026.";
            }
            else
            {
                date = parsedDate;
            }

            if (string.IsNullOrWhiteSpace(descriptionRaw))
            {
                errors[TransactionFields.Description] = "Description is required.";
            }

            try
            {
                var correctedAmount = rowCorrections.TryGetValue(TransactionFields.Amount, out var value)
                    ? value
                    : null;

                if (!string.IsNullOrEmpty(amountHeader) || !string.IsNullOrWhiteSpace(correctedAmount))
                {
                    if (!ValueParsers.TryParseAmount(string.IsNullOrWhiteSpace(correctedAmount) ? amountRaw : correctedAmount, out var parsedAmount))
                    {
                        errors[TransactionFields.Amount] = "Amount must be a number, e.g. -450.00 or R 1,250.00.";
                    }
                    else
                    {
                        amount = parsedAmount;
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(debitRaw) && string.IsNullOrWhiteSpace(creditRaw))
                    {
                        errors[TransactionFields.Amount] = "Provide a debit and/or credit amount.";
                    }
                    else
                    {
                        amount = ValueParsers.CombineDebitCredit(debitRaw, creditRaw);
                    }
                }
            }
            catch (FormatException)
            {
                errors[TransactionFields.Amount] = "Amount must be a number, e.g. -450.00 or R 1,250.00.";
            }

            decimal? balance = null;
            if (!string.IsNullOrWhiteSpace(balanceRaw))
            {
                if (!ValueParsers.TryParseAmount(balanceRaw, out var parsedBalance))
                {
                    errors[TransactionFields.Balance] = "Balance must be a number, or leave it blank.";
                }
                else
                {
                    balance = parsedBalance;
                }
            }

            ParsedTransaction? parsed = null;
            if (errors.Count == 0 && date is not null && amount is not null)
            {
                parsed = new ParsedTransaction
                {
                    SourceRowNumber = row.SourceRowNumber,
                    Date = date.Value,
                    Description = descriptionRaw.Trim(),
                    Amount = amount.Value,
                    Category = EmptyToNull(Resolve(row, categoryHeader, TransactionFields.Category, rowCorrections)),
                    Reference = EmptyToNull(Resolve(row, referenceHeader, TransactionFields.Reference, rowCorrections)),
                    Counterparty = EmptyToNull(Resolve(row, counterpartyHeader, TransactionFields.Counterparty, rowCorrections)),
                    Balance = balance
                };
            }

            results.Add(new ValidatedImportRow
            {
                SourceRowNumber = row.SourceRowNumber,
                Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [TransactionFields.Date] = dateRaw,
                    [TransactionFields.Description] = descriptionRaw,
                    [TransactionFields.Amount] = string.IsNullOrEmpty(amountHeader)
                        ? $"{debitRaw}|{creditRaw}"
                        : amountRaw,
                    [TransactionFields.Category] = Resolve(row, categoryHeader, TransactionFields.Category, rowCorrections),
                    [TransactionFields.Reference] = Resolve(row, referenceHeader, TransactionFields.Reference, rowCorrections),
                    [TransactionFields.Counterparty] = Resolve(row, counterpartyHeader, TransactionFields.Counterparty, rowCorrections),
                    [TransactionFields.Balance] = balanceRaw
                },
                DisplayAmount = string.IsNullOrEmpty(amountHeader)
                    ? FirstNonEmpty(amountRaw, debitRaw, creditRaw)
                    : amountRaw,
                Errors = errors,
                Parsed = parsed
            });
        }

        return results;
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

    private static string Resolve(
        CsvRow row,
        string? header,
        string field,
        IReadOnlyDictionary<string, string> corrections)
    {
        if (corrections.TryGetValue(field, out var corrected) && corrected is not null)
        {
            return corrected;
        }

        if (header is null)
        {
            return string.Empty;
        }

        return row.Values.TryGetValue(header, out var value) ? value : string.Empty;
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
}

public sealed class ValidatedImportRow
{
    public required int SourceRowNumber { get; init; }
    public required IReadOnlyDictionary<string, string> Fields { get; init; }
    public required string DisplayAmount { get; init; }
    public required IReadOnlyDictionary<string, string> Errors { get; init; }
    public ParsedTransaction? Parsed { get; init; }
    public bool IsValid => Errors.Count == 0 && Parsed is not null;
}

public sealed class ParsedTransaction
{
    public required int SourceRowNumber { get; init; }
    public required DateOnly Date { get; init; }
    public required string Description { get; init; }
    public required decimal Amount { get; init; }
    public string? Category { get; init; }
    public string? Reference { get; init; }
    public string? Counterparty { get; init; }
    public decimal? Balance { get; init; }
}
