namespace CruxAI.Core.Entities;

public sealed class Transaction
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ImportJobId { get; set; }

    /// <summary>Stable id for this imported line (same job + source row always yields the same value).</summary>
    public string RowId { get; set; } = string.Empty;

    public int SourceRowNumber { get; set; }
    public DateOnly Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string? Category { get; set; }
    public string? Reference { get; set; }
    public string? Counterparty { get; set; }
    public DateTime ImportedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public ImportJob ImportJob { get; set; } = null!;
}
