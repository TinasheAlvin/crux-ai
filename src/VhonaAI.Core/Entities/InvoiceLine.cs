namespace VhonaAI.Core.Entities;

public sealed class InvoiceLine : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid InvoiceId { get; set; }

    /// <summary>Stable id for this line, from its source row. Distinct from the invoice header RowId.</summary>
    public string RowId { get; set; } = string.Empty;

    public int LineNumber { get; set; }
    public string? Description { get; set; }
    public string? ItemCode { get; set; }
    public decimal Amount { get; set; }
    public decimal? Quantity { get; set; }

    public Invoice Invoice { get; set; } = null!;
}
