namespace VhonaAI.Core.Calling;

public sealed class SampleLedger
{
    public required string BookId { get; init; }
    public required string DisplayName { get; init; }
    public required DateOnly AsAt { get; init; }
    public IReadOnlyList<WhoToCallCustomerBook> Customers { get; init; } = [];
    public IReadOnlyList<SampleCreditNote> CreditNotes { get; init; } = [];
    public IReadOnlyList<SampleBankRow> Transactions { get; init; } = [];
}

public sealed class SampleCreditNote
{
    public required string RowId { get; init; }
    public required string InvoiceRowId { get; init; }
    public required string Number { get; init; }
    public required DateOnly IssuedDate { get; init; }
    public required decimal Amount { get; init; }
    public required string Reason { get; init; }
}

public sealed record SampleBankRow(
    string RowId,
    DateOnly Date,
    DateOnly BookedDate,
    string Description,
    decimal Amount,
    string Category,
    string Counterparty,
    decimal Balance = 0,
    int SourceRowNumber = 0);

public static class SampleBookCatalog
{
    public const string KarooId = "karoo";
    public const string AxumId = "axum";

    public static SampleLedger Build(string? bookId) => bookId switch
    {
        KarooId => KarooKitchenBook.Build(),
        AxumId => AxumHomeBook.Build(),
        _ => throw new InvalidOperationException("Choose Karoo Kitchen Group or Axum Home.")
    };
}

static class SampleIds
{
    public static Guid For(string key)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        hash[7] = (byte)((hash[7] & 0x0F) | 0x40);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16));
    }
}

static class SampleBank
{
    public static IReadOnlyList<SampleBankRow> NumberAndBalance(
        IReadOnlyList<SampleBankRow> rows,
        decimal opening,
        DateOnly? pinMonth = null,
        decimal? pinBalance = null)
    {
        var duplicate = rows.GroupBy(row => row.RowId, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate sample row {duplicate.Key}.");
        }

        var tooLong = rows.FirstOrDefault(row => row.RowId.Length > 64 || row.Description.Length > 500);
        if (tooLong is not null)
        {
            throw new InvalidOperationException($"Sample row is too long: {tooLong.RowId}.");
        }

        var ordered = rows
            .OrderBy(row => row.Date)
            .ThenBy(row => row.RowId, StringComparer.Ordinal)
            .ToList();
        decimal running = opening;
        var built = new List<SampleBankRow>(ordered.Count);
        var number = 1;
        foreach (var row in ordered)
        {
            running += row.Amount;
            built.Add(row with { Balance = running, SourceRowNumber = number++ });
        }

        if (pinMonth is DateOnly month && pinBalance is decimal target)
        {
            var last = built.Last(row => row.Date.Year == month.Year && row.Date.Month == month.Month);
            var offset = target - last.Balance;
            built = built.Select(row => row with { Balance = row.Balance + offset }).ToList();
        }

        return built;
    }

    public static DateOnly Booked(DateOnly date)
    {
        var next = date.AddDays(1);
        return next.Month == date.Month ? next : date;
    }
}
