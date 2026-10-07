using System.Security.Cryptography;
using System.Text;

namespace VhonaAI.Core.Parsing;

public static class RowIdFactory
{
    public static string ForImportLine(Guid importJobId, int sourceRowNumber)
    {
        RequireRow(sourceRowNumber);
        return $"imp_{importJobId:N}_r{sourceRowNumber}";
    }

    public static string ForInvoice(Guid importJobId, int sourceRowNumber) =>
        Prefixed("inv", importJobId, sourceRowNumber);

    public static string ForInvoiceLine(Guid importJobId, int sourceRowNumber) =>
        Prefixed("line", importJobId, sourceRowNumber);

    public static string ForPayment(Guid importJobId, int sourceRowNumber) =>
        Prefixed("pay", importJobId, sourceRowNumber);

    public static string ForCreditNote(Guid importJobId, int sourceRowNumber) =>
        Prefixed("cn", importJobId, sourceRowNumber);

    /// <summary>
    /// Stable customer id for one business and one normalised name.
    /// A later import of the same name cites the same RowId.
    /// </summary>
    public static string ForCustomer(Guid organizationId, string normalizedName)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organisation is required.", nameof(organizationId));
        }

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new ArgumentException("Customer name is required.", nameof(normalizedName));
        }

        var payload = Encoding.UTF8.GetBytes(organizationId.ToString("N") + "|" + normalizedName);
        var hash = SHA256.HashData(payload);
        return "cust_" + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static string Prefixed(string prefix, Guid importJobId, int sourceRowNumber)
    {
        RequireRow(sourceRowNumber);
        return $"{prefix}_{importJobId:N}_r{sourceRowNumber}";
    }

    private static void RequireRow(int sourceRowNumber)
    {
        if (sourceRowNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRowNumber));
        }
    }
}
