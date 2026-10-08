namespace VhonaAI.Core.Parsing;

public enum ImportedLedgerRowKind
{
    Invoice = 0,
    Payment = 1,
    CreditNote = 2
}

public static class LedgerRowKindParser
{
    public static bool TryParse(string? raw, out ImportedLedgerRowKind kind)
    {
        kind = ImportedLedgerRowKind.Invoice;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = string.Join(' ', raw.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        switch (text)
        {
            case "invoice":
            case "inv":
            case "tax invoice":
            case "sales invoice":
                kind = ImportedLedgerRowKind.Invoice;
                return true;
            case "payment":
            case "receipt":
            case "payment received":
                kind = ImportedLedgerRowKind.Payment;
                return true;
            case "credit":
            case "credit note":
            case "creditnote":
            case "cn":
                kind = ImportedLedgerRowKind.CreditNote;
                return true;
            default:
                return false;
        }
    }
}
