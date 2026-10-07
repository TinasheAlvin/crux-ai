namespace VhonaAI.Core.Calling;

/// <summary>
/// Pencilled defaults from 7 Oct 2026: 2 missed cycles, a 50% drop over 2 months, and at least 3 invoices.
/// The who-to-call rules that read these values are a later phase.
/// </summary>
public static class CallThresholdDefaults
{
    public const int StoppedMissedCycles = 2;
    public const decimal DroppedPercent = 50m;
    public const int DroppedMonths = 2;
    public const int MinimumInvoiceHistory = 3;
}

public static class CallThresholdValidator
{
    public const int MinCycles = 1;
    public const int MaxCycles = 24;
    public const decimal MinDroppedPercent = 1m;
    public const decimal MaxDroppedPercent = 100m;
    public const int MinMonths = 1;
    public const int MaxMonths = 24;
    public const int MinHistory = 1;
    public const int MaxHistory = 36;

    public static IReadOnlyList<string> Validate(
        int stoppedMissedCycles,
        decimal droppedPercent,
        int droppedMonths,
        int minimumInvoiceHistory)
    {
        var errors = new List<string>();

        if (stoppedMissedCycles < MinCycles || stoppedMissedCycles > MaxCycles)
        {
            errors.Add($"Missed cycles must be between {MinCycles} and {MaxCycles}.");
        }

        if (droppedPercent < MinDroppedPercent || droppedPercent > MaxDroppedPercent)
        {
            errors.Add($"Drop percent must be between {MinDroppedPercent:0} and {MaxDroppedPercent:0}.");
        }

        if (droppedMonths < MinMonths || droppedMonths > MaxMonths)
        {
            errors.Add($"Drop months must be between {MinMonths} and {MaxMonths}.");
        }

        if (minimumInvoiceHistory < MinHistory || minimumInvoiceHistory > MaxHistory)
        {
            errors.Add($"Minimum invoice history must be between {MinHistory} and {MaxHistory}.");
        }

        return errors;
    }
}
