namespace VhonaAI.Core.Entities;

/// <summary>
/// Who-to-call thresholds used when a business has not saved its own row.
/// One row for the whole platform. Business owners cannot edit it.
/// </summary>
public sealed class PlatformCallDefaults
{
    public Guid Id { get; set; }
    public int StoppedMissedCycles { get; set; }
    public decimal DroppedPercent { get; set; }
    public int DroppedMonths { get; set; }
    public int MinimumInvoiceHistory { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
