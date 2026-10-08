namespace VhonaAI.Core.Entities;

public sealed class Payment : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? DataSourceId { get; set; }
    public Guid? ImportJobId { get; set; }

    public string RowId { get; set; } = string.Empty;
    public DateOnly PaidDate { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public int SourceRowNumber { get; set; }
    public DateTime ImportedAt { get; set; }

    public Invoice? Invoice { get; set; }
    public Customer? Customer { get; set; }
    public DataSource? DataSource { get; set; }
    public ImportJob? ImportJob { get; set; }
}
