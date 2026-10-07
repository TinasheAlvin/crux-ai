namespace VhonaAI.Core.Mapping;

public static class TransactionFields
{
    public const string Ignore = "Ignore";
    public const string Date = "Date";
    public const string Description = "Description";
    public const string Amount = "Amount";
    public const string Debit = "Debit";
    public const string Credit = "Credit";
    public const string Category = "Category";
    public const string Reference = "Reference";
    public const string Counterparty = "Counterparty";
    public const string Balance = "Balance";
    public const string BookedDate = "BookedDate";

    public static readonly IReadOnlyList<string> All =
    [
        Ignore,
        Date,
        Description,
        Amount,
        Debit,
        Credit,
        Category,
        Reference,
        Counterparty,
        Balance,
        BookedDate
    ];

    public static readonly IReadOnlyList<string> Assignable = All.Where(f => f != Ignore).ToArray();
}
