namespace VhonaAI.Core.Entities;

public sealed class Transaction : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ImportJobId { get; set; }
    public Guid? DataSourceId { get; set; }

    /// <summary>Stable id for this imported line (same job + source row always yields the same value).</summary>
    public string RowId { get; set; } = string.Empty;

    public int SourceRowNumber { get; set; }

    /// <summary>Document date. Health and why still use this date.</summary>
    public DateOnly Date { get; set; }

    /// <summary>When the row was booked, if the file has a second date. Null on older imports.</summary>
    public DateOnly? BookedDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string? Category { get; set; }
    public string? Reference { get; set; }
    public string? Counterparty { get; set; }

    /// <summary>Optional running/closing balance from the source file. Null when cash was not mapped or the cell was empty.</summary>
    public decimal? Balance { get; set; }

    public DateTime ImportedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public ImportJob ImportJob { get; set; } = null!;
    public DataSource? DataSource { get; set; }
}
