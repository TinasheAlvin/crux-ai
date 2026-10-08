namespace VhonaAI.Core.Entities;

public sealed class ImportJob : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CreatedByUserId { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public long ByteSize { get; set; }

    public ImportStatus Status { get; set; } = ImportStatus.Uploaded;

    /// <summary>Transactions keeps the original bank-style import. Invoices is the debtors file.</summary>
    public ImportKind Kind { get; set; } = ImportKind.Transactions;

    /// <summary>JSON object: source header → target field name.</summary>
    public string? MappingJson { get; set; }

    /// <summary>JSON object: row number → { field → corrected value }.</summary>
    public string? CorrectionsJson { get; set; }

    public int? ImportedRowCount { get; set; }
    public string? ErrorSummary { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public AppUser CreatedByUser { get; set; } = null!;
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}

public enum ImportStatus
{
    Uploaded = 0,
    Mapped = 1,
    Validated = 2,
    Imported = 3,
    Failed = 4
}

public enum ImportKind
{
    Transactions = 0,
    Invoices = 1
}
