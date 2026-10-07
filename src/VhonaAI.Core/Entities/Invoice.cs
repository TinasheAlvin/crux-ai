namespace VhonaAI.Core.Entities;

public sealed class Invoice : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? DataSourceId { get; set; }
    public Guid? ImportJobId { get; set; }

    /// <summary>Stable id for the invoice header, taken from the first source row in the import.</summary>
    public string RowId { get; set; } = string.Empty;

    public string Number { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly? BookedDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountDue { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Open;
    public DateOnly? PaidDate { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string? Terms { get; set; }
    public int SourceRowNumber { get; set; }
    public DateTime ImportedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public DataSource? DataSource { get; set; }
    public ImportJob? ImportJob { get; set; }
    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<CreditNote> CreditNotes { get; set; } = new List<CreditNote>();
}
