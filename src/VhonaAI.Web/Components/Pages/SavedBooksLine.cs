namespace VhonaAI.Web;

public static class SavedBooksLine
{
    public static string? Format(int invoiceCount, int transactionCount)
    {
        if (invoiceCount == 0 && transactionCount == 0)
        {
            return null;
        }

        return $"{Noun(invoiceCount, "invoice")} and {Noun(transactionCount, "transaction")} saved";
    }

    private static string Noun(int count, string singular) =>
        count == 1 ? $"1 {singular}" : $"{count} {singular}s";
}
