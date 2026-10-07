namespace VhonaAI.Core.Entities;

/// <summary>
/// Per-business who-to-call thresholds. The detection rules and the settings screen come later;
/// this row is only the stored choice.
/// </summary>
public sealed class CallThresholdSettings : IOrganizationOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }

    /// <summary>Missed billing cycles before a customer is "stopped".</summary>
    public int StoppedMissedCycles { get; set; }

    /// <summary>Drop size as a percent. 50 means a 50% drop, not a fraction of 0.50.</summary>
    public decimal DroppedPercent { get; set; }

    /// <summary>How many consecutive months the drop must last.</summary>
    public int DroppedMonths { get; set; }

    /// <summary>Minimum invoices before stopped or dropped can apply.</summary>
    public int MinimumInvoiceHistory { get; set; }

    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public Organization Organization { get; set; } = null!;
}
