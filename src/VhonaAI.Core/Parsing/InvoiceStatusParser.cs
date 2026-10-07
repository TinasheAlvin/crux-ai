using VhonaAI.Core.Entities;

namespace VhonaAI.Core.Parsing;

public static class InvoiceStatusParser
{
    public static bool TryParse(string? raw, out InvoiceStatus status)
    {
        status = InvoiceStatus.Open;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = string.Join(' ', raw.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        switch (text)
        {
            case "draft":
                status = InvoiceStatus.Draft;
                return true;
            case "open":
            case "unpaid":
            case "outstanding":
            case "overdue":
            case "partial":
            case "part paid":
            case "partially paid":
            case "authorised":
            case "authorized":
            case "approved":
            case "sent":
                status = InvoiceStatus.Open;
                return true;
            case "paid":
            case "settled":
            case "closed":
                status = InvoiceStatus.Paid;
                return true;
            case "void":
            case "voided":
            case "cancelled":
            case "canceled":
                status = InvoiceStatus.Void;
                return true;
            case "credited":
            case "credit":
            case "credit note":
                status = InvoiceStatus.Credited;
                return true;
            default:
                return false;
        }
    }
}
