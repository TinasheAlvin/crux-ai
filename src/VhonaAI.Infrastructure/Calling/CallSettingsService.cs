using VhonaAI.Application.Calling;
using VhonaAI.Core.Calling;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Identity;
using VhonaAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Infrastructure.Calling;

public sealed class CallSettingsService : ICallSettingsAppService
{
    private readonly VhonaDbContext _db;
    private readonly ICurrentUser _currentUser;

    public CallSettingsService(VhonaDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<CallThresholdSettingsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        RequireMember();
        var stored = await _db.CallThresholdSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.OrganizationId == _currentUser.OrganizationId, cancellationToken);
        if (stored is null)
        {
            return Defaults();
        }

        return new CallThresholdSettingsDto(
            stored.StoppedMissedCycles,
            stored.DroppedPercent,
            stored.DroppedMonths,
            stored.MinimumInvoiceHistory,
            IsCustom: true);
    }

    public async Task<CallThresholdSettingsDto> UpdateAsync(
        CallThresholdSettingsUpdate update,
        CancellationToken cancellationToken = default)
    {
        RequireOwner();
        var errors = CallThresholdValidator.Validate(
            update.StoppedMissedCycles,
            update.DroppedPercent,
            update.DroppedMonths,
            update.MinimumInvoiceHistory);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        var stored = await _db.CallThresholdSettings
            .FirstOrDefaultAsync(item => item.OrganizationId == _currentUser.OrganizationId, cancellationToken);
        if (stored is null)
        {
            stored = new CallThresholdSettings
            {
                Id = Guid.NewGuid(),
                OrganizationId = _currentUser.OrganizationId
            };
            _db.CallThresholdSettings.Add(stored);
        }

        stored.StoppedMissedCycles = update.StoppedMissedCycles;
        stored.DroppedPercent = update.DroppedPercent;
        stored.DroppedMonths = update.DroppedMonths;
        stored.MinimumInvoiceHistory = update.MinimumInvoiceHistory;
        stored.UpdatedAt = DateTime.UtcNow;
        stored.UpdatedByUserId = _currentUser.UserId;
        await _db.SaveChangesAsync(cancellationToken);

        return new CallThresholdSettingsDto(
            stored.StoppedMissedCycles,
            stored.DroppedPercent,
            stored.DroppedMonths,
            stored.MinimumInvoiceHistory,
            IsCustom: true);
    }

    private void RequireMember()
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.HasOrganization)
        {
            throw new InvalidOperationException("Sign in to a business to continue.");
        }
    }

    private void RequireOwner()
    {
        RequireMember();
        if (!_currentUser.IsOwner)
        {
            throw new InvalidOperationException("Only an owner can change who-to-call thresholds.");
        }
    }

    private static CallThresholdSettingsDto Defaults() =>
        new(
            CallThresholdDefaults.StoppedMissedCycles,
            CallThresholdDefaults.DroppedPercent,
            CallThresholdDefaults.DroppedMonths,
            CallThresholdDefaults.MinimumInvoiceHistory,
            IsCustom: false);
}
