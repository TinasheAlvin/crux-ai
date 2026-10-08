namespace VhonaAI.Core.Entities;

public sealed class CreditNote : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? DataSourceId { get; set; }
    public Guid? ImportJobId { get; set; }

    public string RowId { get; set; } = string.Empty;
    public string? Number { get; set; }
    public DateOnly IssuedDate { get; set; }

    /// <summary>Positive amount that reduces what the customer owes. The file's amount due is not recomputed.</summary>
    public decimal Amount { get; set; }

    public string? Reason { get; set; }
    public int SourceRowNumber { get; set; }
    public DateTime ImportedAt { get; set; }

    public Invoice? Invoice { get; set; }
    public Customer? Customer { get; set; }
    public DataSource? DataSource { get; set; }
    public ImportJob? ImportJob { get; set; }
}
