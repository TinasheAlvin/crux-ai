namespace VhonaAI.Application.Calling;

public interface ICallSettingsAppService
{
    Task<CallThresholdSettingsDto> GetAsync(CancellationToken cancellationToken = default);

    Task<CallThresholdSettingsDto> UpdateAsync(
        CallThresholdSettingsUpdate update,
        CancellationToken cancellationToken = default);
}

public sealed record CallThresholdSettingsDto(
    int StoppedMissedCycles,
    decimal DroppedPercent,
    int DroppedMonths,
    int MinimumInvoiceHistory,
    bool IsCustom);

public sealed record CallThresholdSettingsUpdate(
    int StoppedMissedCycles,
    decimal DroppedPercent,
    int DroppedMonths,
    int MinimumInvoiceHistory);
