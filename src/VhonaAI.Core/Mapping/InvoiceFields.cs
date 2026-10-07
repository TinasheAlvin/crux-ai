namespace VhonaAI.Core.Mapping;

public static class InvoiceFields
{
    public const string Ignore = "Ignore";
    public const string CustomerName = "CustomerName";
    public const string ContactPerson = "ContactPerson";
    public const string Phone = "Phone";
    public const string Email = "Email";
    public const string CustomerSourceId = "CustomerSourceId";
    public const string InvoiceNumber = "InvoiceNumber";
    public const string InvoiceDate = "InvoiceDate";
    public const string BookedDate = "BookedDate";
    public const string DueDate = "DueDate";
    public const string Amount = "Amount";
    public const string AmountDue = "AmountDue";
    public const string Status = "Status";
    public const string PaidDate = "PaidDate";
    public const string Currency = "Currency";
    public const string Terms = "Terms";
    public const string LineDescription = "LineDescription";
    public const string ItemCode = "ItemCode";
    public const string LineAmount = "LineAmount";
    public const string Quantity = "Quantity";
    public const string RowType = "RowType";
    public const string PaymentAmount = "PaymentAmount";
    public const string CreditAmount = "CreditAmount";
    public const string CreditNoteNumber = "CreditNoteNumber";

    public static readonly IReadOnlyList<string> All =
    [
        Ignore,
        CustomerName,
        ContactPerson,
        Phone,
        Email,
        CustomerSourceId,
        InvoiceNumber,
        InvoiceDate,
        BookedDate,
        DueDate,
        Amount,
        AmountDue,
        Status,
        PaidDate,
        Currency,
        Terms,
        LineDescription,
        ItemCode,
        LineAmount,
        Quantity,
        RowType,
        PaymentAmount,
        CreditAmount,
        CreditNoteNumber
    ];
}
