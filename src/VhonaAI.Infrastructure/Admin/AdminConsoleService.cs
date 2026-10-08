using System.Security.Cryptography;
using System.Text.Json;
using VhonaAI.Application.Admin;
using VhonaAI.Application.Calling;
using VhonaAI.Core.Analytics;
using VhonaAI.Core.Calling;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Identity;
using VhonaAI.Core.Storage;
using VhonaAI.Infrastructure.Data;
using VhonaAI.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Infrastructure.Admin;

public sealed class AdminConsoleService : IAdminConsoleService
{
    public const int InspectRowCap = 200;

    private static readonly JsonSerializerOptions ExportJson = new() { WriteIndented = true };

    private readonly VhonaDbContext _db;
    private readonly IVhonaAdmin _admin;
    private readonly IFileStorage _storage;
    private readonly IEventLog _events;

    public AdminConsoleService(VhonaDbContext db, IVhonaAdmin admin, IFileStorage storage, IEventLog? events = null)
    {
        _db = db;
        _admin = admin;
        _storage = storage;
        _events = events ?? NullEventLog.Instance;
    }

    public async Task<AdminOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        var overview = new AdminOverview(
            await _db.Organizations.IgnoreQueryFilters().CountAsync(cancellationToken),
            await _db.Users.CountAsync(cancellationToken),
            await _db.Organizations.IgnoreQueryFilters().CountAsync(item => item.DisabledAt != null, cancellationToken),
            await _db.Invitations.IgnoreQueryFilters().CountAsync(
                item => item.AcceptedAt == null && item.RevokedAt == null && item.ExpiresAt > DateTime.UtcNow,
                cancellationToken));
        await AuditAsync("console.open", null, null, "Opened the admin console.", cancellationToken);
        return overview;
    }

    public async Task<IReadOnlyList<AdminBusinessSummary>> SearchBusinessesAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        var businesses = await _db.Organizations.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken);
        var memberships = await _db.Memberships.IgnoreQueryFilters().AsNoTracking().Include(item => item.User).ToListAsync(cancellationToken);
        var invoiceCounts = await _db.Invoices.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(item => item.OrganizationId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var transactionCounts = await _db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(item => item.OrganizationId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var lastImports = await _db.ImportJobs.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(item => item.OrganizationId)
            .Select(group => new { group.Key, At = group.Max(item => item.CreatedAt) })
            .ToListAsync(cancellationToken);

        var needle = (query ?? string.Empty).Trim();
        var rows = businesses
            .Where(item => needle.Length == 0 || item.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Name)
            .Select(item =>
            {
                var members = memberships.Where(member => member.OrganizationId == item.Id).ToList();
                var owner = members.FirstOrDefault(member => member.Role == MembershipRole.Owner);
                return new AdminBusinessSummary(
                    item.Id,
                    item.Name,
                    item.CreatedAt,
                    item.DisabledAt is not null,
                    owner?.User.Email,
                    members.Count,
                    invoiceCounts.FirstOrDefault(count => count.Key == item.Id)?.Count ?? 0,
                    transactionCounts.FirstOrDefault(count => count.Key == item.Id)?.Count ?? 0,
                    lastImports.FirstOrDefault(import => import.Key == item.Id)?.At);
            })
            .ToList();

        await AuditAsync("businesses.list", null, null, $"Listed {rows.Count} businesses.", cancellationToken);
        return rows;
    }

    public async Task<AdminBusinessDetail> GetBusinessAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        var organization = await RequireBusinessAsync(organizationId, cancellationToken);
        var members = await _db.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.User)
            .Where(item => item.OrganizationId == organizationId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        var invites = await _db.Invitations.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.AcceptedAt == null)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        var sources = await _db.DataSources.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.OrganizationId == organizationId)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        var imports = await _db.ImportJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.OrganizationId == organizationId)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        await AuditAsync("business.view", organizationId, organizationId.ToString(), $"Viewed {organization.Name}.", cancellationToken);

        return new AdminBusinessDetail(
            organization.Id,
            organization.Name,
            organization.CreatedAt,
            organization.DisabledAt is not null,
            await _db.Customers.IgnoreQueryFilters().CountAsync(item => item.OrganizationId == organizationId, cancellationToken),
            await _db.Invoices.IgnoreQueryFilters().CountAsync(item => item.OrganizationId == organizationId, cancellationToken),
            await _db.Transactions.IgnoreQueryFilters().CountAsync(item => item.OrganizationId == organizationId, cancellationToken),
            members.Select(item => new AdminMemberRow(
                item.Id, item.UserId, item.User.Email, item.User.DisplayName, item.Role.ToString(), item.User.DisabledAt is not null)).ToList(),
            invites.Select(item => ToInvite(item, organization.Name)).ToList(),
            sources.Select(item => new AdminDataSourceRow(item.Id, item.Name, item.Kind.ToString(), item.CreatedAt)).ToList(),
            imports.Select(ToImport).ToList(),
            await ThresholdsForAsync(organizationId, cancellationToken));
    }

    public async Task RenameBusinessAsync(Guid organizationId, string name, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        using var scope = AdminDataAccess.Open(_admin.UserId);
        var organization = await RequireBusinessAsync(organizationId, cancellationToken);
        var businessName = BusinessNameRules.Normalize(name);
        var previous = organization.Name;
        organization.Name = businessName;
        Audit("business.rename", organizationId, organizationId.ToString(), $"Renamed '{previous}' to '{businessName}'.");
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetBusinessDisabledAsync(Guid organizationId, bool disabled, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        using var scope = AdminDataAccess.Open(_admin.UserId);
        var organization = await RequireBusinessAsync(organizationId, cancellationToken);
        organization.DisabledAt = disabled ? DateTime.UtcNow : null;
        Audit(disabled ? "business.disable" : "business.enable", organizationId, organizationId.ToString(),
            disabled ? $"Disabled {organization.Name}." : $"Re-enabled {organization.Name}.");
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteBusinessAsync(Guid organizationId, string typedName, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        using var scope = AdminDataAccess.Open(_admin.UserId);
        var organization = await RequireBusinessAsync(organizationId, cancellationToken);
        if (!string.Equals(organization.Name, (typedName ?? string.Empty).Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Type the business name exactly to confirm deletion.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var paths = await _db.ImportJobs.IgnoreQueryFilters()
            .Where(item => item.OrganizationId == organizationId)
            .Select(item => item.StoragePath)
            .ToListAsync(cancellationToken);
        await DeleteBusinessRowsAsync(organizationId, cancellationToken);
        _db.Organizations.Remove(organization);
        Audit("business.delete", organizationId, organizationId.ToString(), $"Deleted {organization.Name} and its data.");
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var path in paths)
        {
            await _storage.DeleteAsync(path, cancellationToken);
        }
    }

    public async Task<AdminExportFile> ExportBusinessAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        var organization = await RequireBusinessAsync(organizationId, cancellationToken);
        var payload = new
        {
            exportedAt = DateTime.UtcNow,
            organization = new { organization.Id, organization.Name, organization.CreatedAt, organization.DisabledAt },
            memberships = await _db.Memberships.IgnoreQueryFilters().AsNoTracking().Include(item => item.User)
                .Where(item => item.OrganizationId == organizationId)
                .Select(item => new { item.Id, item.UserId, item.User.Email, item.User.DisplayName, Role = item.Role.ToString(), item.CreatedAt })
                .ToListAsync(cancellationToken),
            importJobs = await _db.ImportJobs.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken),
            columnMappingProfiles = await _db.ColumnMappingProfiles.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken),
            transactions = await _db.Transactions.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken),
            whyAnswers = await _db.WhyAnswers.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken),
            whyCitations = await CitationsForWhyAsync(organizationId, cancellationToken),
            morningBriefPreferences = await _db.MorningBriefPreferences.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken),
            morningBriefs = await _db.MorningBriefs.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken),
            morningBriefCitations = await CitationsForBriefsAsync(organizationId, cancellationToken),
            dataSources = await _db.DataSources.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken),
            customers = await _db.Customers.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken),
            invoices = await _db.Invoices.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken),
            invoiceLines = await _db.InvoiceLines.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken),
            payments = await _db.Payments.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken),
            creditNotes = await _db.CreditNotes.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken),
            invitations = await _db.Invitations.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken),
            callThresholdSettings = await _db.CallThresholdSettings.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken),
            callActions = await _db.CallCustomerActions.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken),
            reminderDrafts = await _db.ReminderDrafts.IgnoreQueryFilters().AsNoTracking().Where(item => item.OrganizationId == organizationId).ToListAsync(cancellationToken)
        };

        await AuditAsync("business.export", organizationId, organizationId.ToString(), $"Exported {organization.Name}.", cancellationToken);
        var safe = new string(organization.Name.Where(char.IsLetterOrDigit).ToArray());
        if (safe.Length == 0)
        {
            safe = "business";
        }

        return new AdminExportFile(
            JsonSerializer.SerializeToUtf8Bytes(payload, ExportJson),
            $"vhona-{safe}-{DateTime.UtcNow:yyyyMMdd}.json");
    }

    public async Task<IReadOnlyList<AdminUserSummary>> SearchUsersAsync(string? query, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        var needle = (query ?? string.Empty).Trim();
        var users = await _db.Users.AsNoTracking().OrderBy(item => item.Email).ToListAsync(cancellationToken);
        var memberships = await _db.Memberships.IgnoreQueryFilters().AsNoTracking().Include(item => item.Organization).ToListAsync(cancellationToken);
        var rows = users
            .Where(item => needle.Length == 0
                           || item.Email.Contains(needle, StringComparison.OrdinalIgnoreCase)
                           || item.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Select(item =>
            {
                var names = memberships.Where(member => member.UserId == item.Id)
                    .Select(member => $"{member.Organization.Name} ({member.Role})")
                    .ToList();
                return new AdminUserSummary(item.Id, item.Email, item.DisplayName, item.DisabledAt is not null, string.Join(", ", names));
            })
            .ToList();
        await AuditAsync("users.list", null, null, $"Listed {rows.Count} users.", cancellationToken);
        return rows;
    }

    public async Task<AdminUserDetail> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(item => item.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");
        var memberships = await _db.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.Organization)
            .Where(item => item.UserId == userId)
            .ToListAsync(cancellationToken);
        var email = user.Email.Trim().ToLowerInvariant();
        var invites = await _db.Invitations.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.Organization)
            .Where(item => item.Email == email && item.AcceptedAt == null)
            .ToListAsync(cancellationToken);
        await AuditAsync("user.view", memberships.FirstOrDefault()?.OrganizationId, userId.ToString(), $"Viewed {user.Email}.", cancellationToken);
        return new AdminUserDetail(
            user.Id,
            user.Email,
            user.DisplayName,
            user.DisabledAt is not null,
            user.ExternalId,
            memberships.Select(item => new AdminUserMembership(item.Id, item.OrganizationId, item.Organization.Name, item.Role.ToString())).ToList(),
            invites.Select(item => ToInvite(item, item.Organization.Name)).ToList());
    }

    public async Task SetUserRoleAsync(Guid membershipId, string role, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        if (!Enum.TryParse<MembershipRole>(role, ignoreCase: true, out var parsed) || parsed is not (MembershipRole.Owner or MembershipRole.Member))
        {
            throw new InvalidOperationException("Role must be Owner or Member.");
        }

        using var scope = AdminDataAccess.Open(_admin.UserId);
        var membership = await _db.Memberships.IgnoreQueryFilters().Include(item => item.User).Include(item => item.Organization)
            .FirstOrDefaultAsync(item => item.Id == membershipId, cancellationToken)
            ?? throw new InvalidOperationException("Membership not found.");
        if (membership.Role == MembershipRole.Owner && parsed == MembershipRole.Member)
        {
            await EnsureAnotherOwnerAsync(membership.OrganizationId, membership.Id, cancellationToken);
        }

        var previous = membership.Role.ToString();
        membership.Role = parsed;
        Audit("user.role", membership.OrganizationId, membership.UserId.ToString(),
            $"Changed {membership.User.Email} in {membership.Organization.Name} from {previous} to {parsed}.");
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveMemberAsync(Guid membershipId, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        using var scope = AdminDataAccess.Open(_admin.UserId);
        var membership = await _db.Memberships.IgnoreQueryFilters().Include(item => item.User).Include(item => item.Organization)
            .FirstOrDefaultAsync(item => item.Id == membershipId, cancellationToken)
            ?? throw new InvalidOperationException("Membership not found.");
        if (membership.Role == MembershipRole.Owner)
        {
            await EnsureAnotherOwnerAsync(membership.OrganizationId, membership.Id, cancellationToken);
        }

        Audit("user.remove", membership.OrganizationId, membership.UserId.ToString(),
            $"Removed {membership.User.Email} from {membership.Organization.Name}.");
        _db.Memberships.Remove(membership);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AdminInviteIssued> ResendInviteAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        using var scope = AdminDataAccess.Open(_admin.UserId);
        var invite = await _db.Invitations.IgnoreQueryFilters().Include(item => item.Organization)
            .FirstOrDefaultAsync(item => item.Id == invitationId, cancellationToken)
            ?? throw new InvalidOperationException("Invite not found.");
        if (invite.AcceptedAt is not null)
        {
            throw new InvalidOperationException("That invite was already accepted.");
        }

        invite.Token = NewToken();
        invite.ExpiresAt = DateTime.UtcNow.Add(BusinessService.InviteLifetime);
        invite.RevokedAt = null;
        Audit("invite.resend", invite.OrganizationId, invite.Id.ToString(),
            $"Issued a new link for {invite.Email} at {invite.Organization.Name}. Vhona does not email it.");
        await _db.SaveChangesAsync(cancellationToken);
        return new AdminInviteIssued(invite.Id, invite.Email, invite.Token, invite.ExpiresAt);
    }

    public async Task RevokeInviteAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        using var scope = AdminDataAccess.Open(_admin.UserId);
        var invite = await _db.Invitations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == invitationId, cancellationToken)
            ?? throw new InvalidOperationException("Invite not found.");
        invite.RevokedAt = DateTime.UtcNow;
        Audit("invite.revoke", invite.OrganizationId, invite.Id.ToString(), $"Revoked the invite for {invite.Email}.");
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetUserDisabledAsync(Guid userId, bool disabled, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        if (disabled && userId == _admin.UserId)
        {
            throw new InvalidOperationException("You cannot disable your own sign-in.");
        }

        using var scope = AdminDataAccess.Open(_admin.UserId);
        var user = await _db.Users.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");
        user.DisabledAt = disabled ? DateTime.UtcNow : null;
        Audit(disabled ? "user.disable" : "user.enable", null, userId.ToString(),
            disabled ? $"Disabled {user.Email}." : $"Re-enabled {user.Email}.");
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AdminImportDetail> GetImportAsync(Guid importId, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        var job = await _db.ImportJobs.IgnoreQueryFilters().AsNoTracking().Include(item => item.Organization)
            .FirstOrDefaultAsync(item => item.Id == importId, cancellationToken)
            ?? throw new InvalidOperationException("Import not found.");
        var transactions = await _db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.ImportJobId == importId)
            .OrderBy(item => item.SourceRowNumber)
            .Take(InspectRowCap + 1)
            .ToListAsync(cancellationToken);
        var invoices = await _db.Invoices.IgnoreQueryFilters().AsNoTracking().Include(item => item.Customer)
            .Where(item => item.ImportJobId == importId)
            .OrderBy(item => item.SourceRowNumber)
            .Take(InspectRowCap + 1)
            .ToListAsync(cancellationToken);
        var truncated = transactions.Count > InspectRowCap || invoices.Count > InspectRowCap;
        await AuditAsync("import.view", job.OrganizationId, importId.ToString(), $"Viewed import {job.OriginalFileName}.", cancellationToken);
        return new AdminImportDetail(
            job.Id,
            job.OrganizationId,
            job.Organization.Name,
            job.OriginalFileName,
            job.Status.ToString(),
            truncated,
            transactions.Take(InspectRowCap).Select(item => new AdminTransactionRow(
                item.RowId, item.Date, item.BookedDate, item.Description, item.Amount, item.Counterparty)).ToList(),
            invoices.Take(InspectRowCap).Select(item => new AdminInvoiceRow(
                item.RowId, item.Number, item.Customer.Name, item.InvoiceDate, item.Amount, item.AmountDue, item.Status.ToString())).ToList());
    }

    public async Task DeleteImportAsync(Guid importId, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        using var scope = AdminDataAccess.Open(_admin.UserId);
        var job = await _db.ImportJobs.IgnoreQueryFilters().Include(item => item.Organization)
            .FirstOrDefaultAsync(item => item.Id == importId, cancellationToken)
            ?? throw new InvalidOperationException("Import not found.");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var invoiceIds = await _db.Invoices.IgnoreQueryFilters().Where(item => item.ImportJobId == importId).Select(item => item.Id).ToListAsync(cancellationToken);
        await _db.CreditNotes.IgnoreQueryFilters().Where(item => item.ImportJobId == importId || (item.InvoiceId != null && invoiceIds.Contains(item.InvoiceId.Value))).ExecuteDeleteAsync(cancellationToken);
        await _db.Payments.IgnoreQueryFilters().Where(item => item.ImportJobId == importId || (item.InvoiceId != null && invoiceIds.Contains(item.InvoiceId.Value))).ExecuteDeleteAsync(cancellationToken);
        await _db.InvoiceLines.IgnoreQueryFilters().Where(item => invoiceIds.Contains(item.InvoiceId)).ExecuteDeleteAsync(cancellationToken);
        await _db.Invoices.IgnoreQueryFilters().Where(item => item.ImportJobId == importId).ExecuteDeleteAsync(cancellationToken);
        await _db.Transactions.IgnoreQueryFilters().Where(item => item.ImportJobId == importId).ExecuteDeleteAsync(cancellationToken);
        var sourceIds = await _db.DataSources.IgnoreQueryFilters()
            .Where(item => item.ImportJobId == importId)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        if (sourceIds.Count > 0)
        {
            await _db.Customers.IgnoreQueryFilters()
                .Where(item => item.DataSourceId != null && sourceIds.Contains(item.DataSourceId.Value))
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DataSourceId, (Guid?)null), cancellationToken);
        }

        await _db.DataSources.IgnoreQueryFilters().Where(item => item.ImportJobId == importId).ExecuteDeleteAsync(cancellationToken);
        var path = job.StoragePath;
        var name = job.OriginalFileName;
        var organizationId = job.OrganizationId;
        _db.ImportJobs.Remove(job);
        Audit("import.delete", organizationId, importId.ToString(), $"Deleted import {name}.");
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await _storage.DeleteAsync(path, cancellationToken);
    }

    public async Task<CallThresholdSettingsDto> UpdateThresholdsAsync(
        Guid organizationId,
        CallThresholdSettingsUpdate update,
        CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        ValidateThresholds(update);
        using var scope = AdminDataAccess.Open(_admin.UserId);
        await RequireBusinessAsync(organizationId, cancellationToken);
        var stored = await _db.CallThresholdSettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.OrganizationId == organizationId, cancellationToken);
        if (stored is null)
        {
            stored = new CallThresholdSettings { Id = Guid.NewGuid(), OrganizationId = organizationId };
            _db.CallThresholdSettings.Add(stored);
        }

        ApplyThresholds(stored, update);
        Audit("settings.thresholds", organizationId, organizationId.ToString(), "Updated who-to-call thresholds.");
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(stored, isCustom: true);
    }

    public async Task<CallThresholdSettingsDto> GetGlobalDefaultsAsync(CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        var platform = await _db.PlatformCallDefaults.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        await AuditAsync("settings.defaults.view", null, null, "Viewed global who-to-call defaults.", cancellationToken);
        return platform is null
            ? new CallThresholdSettingsDto(
                CallThresholdDefaults.StoppedMissedCycles,
                CallThresholdDefaults.DroppedPercent,
                CallThresholdDefaults.DroppedMonths,
                CallThresholdDefaults.MinimumInvoiceHistory,
                IsCustom: false)
            : new CallThresholdSettingsDto(
                platform.StoppedMissedCycles,
                platform.DroppedPercent,
                platform.DroppedMonths,
                platform.MinimumInvoiceHistory,
                IsCustom: true);
    }

    public async Task<CallThresholdSettingsDto> UpdateGlobalDefaultsAsync(
        CallThresholdSettingsUpdate update,
        CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        ValidateThresholds(update);
        using var scope = AdminDataAccess.Open(_admin.UserId);
        var platform = await _db.PlatformCallDefaults.FirstOrDefaultAsync(cancellationToken);
        if (platform is null)
        {
            platform = new PlatformCallDefaults { Id = Guid.NewGuid() };
            _db.PlatformCallDefaults.Add(platform);
        }

        platform.StoppedMissedCycles = update.StoppedMissedCycles;
        platform.DroppedPercent = update.DroppedPercent;
        platform.DroppedMonths = update.DroppedMonths;
        platform.MinimumInvoiceHistory = update.MinimumInvoiceHistory;
        platform.UpdatedAt = DateTime.UtcNow;
        platform.UpdatedByUserId = _admin.UserId;
        Audit("settings.defaults.update", null, null, "Updated global who-to-call defaults.");
        await _db.SaveChangesAsync(cancellationToken);
        return new CallThresholdSettingsDto(
            platform.StoppedMissedCycles,
            platform.DroppedPercent,
            platform.DroppedMonths,
            platform.MinimumInvoiceHistory,
            IsCustom: true);
    }

    public async Task<IReadOnlyList<AdminPartnerEventRow>> ListPartnerEventsAsync(CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        var rows = _events.Read()
            .OrderByDescending(item => item.Timestamp)
            .Select(item => new AdminPartnerEventRow(item.Name, item.OrgId, item.UserId, item.Timestamp))
            .ToList();
        await AuditAsync("partner-events.view", null, null, $"Viewed {rows.Count} partner events.", cancellationToken);
        return rows;
    }

    public async Task<IReadOnlyList<AdminAuditRow>> ListAuditAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        RequireAdmin();
        take = Math.Clamp(take, 1, 500);
        var rows = await _db.AdminAuditEntries.AsNoTracking()
            .OrderByDescending(item => item.At)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(item => new AdminAuditRow(
            item.Id, item.At, item.ActorEmail, item.Action, item.OrganizationId, item.Target, item.Summary)).ToList();
    }

    private async Task DeleteBusinessRowsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var whyIds = await _db.WhyAnswers.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).Select(item => item.Id).ToListAsync(cancellationToken);
        var briefIds = await _db.MorningBriefs.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).Select(item => item.Id).ToListAsync(cancellationToken);
        await _db.MorningBriefCitations.IgnoreQueryFilters().Where(item => briefIds.Contains(item.MorningBriefId)).ExecuteDeleteAsync(cancellationToken);
        await _db.MorningBriefs.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.MorningBriefPreferences.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.WhyCitations.IgnoreQueryFilters().Where(item => whyIds.Contains(item.WhyAnswerId)).ExecuteDeleteAsync(cancellationToken);
        await _db.WhyAnswers.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.ReminderDrafts.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.CallCustomerActions.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.CreditNotes.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.Payments.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.InvoiceLines.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.Invoices.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.Customers.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.Transactions.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.DataSources.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.ImportJobs.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.ColumnMappingProfiles.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.Invitations.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.CallThresholdSettings.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
        await _db.Memberships.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<List<WhyCitation>> CitationsForWhyAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var ids = await _db.WhyAnswers.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).Select(item => item.Id).ToListAsync(cancellationToken);
        return await _db.WhyCitations.IgnoreQueryFilters().AsNoTracking().Where(item => ids.Contains(item.WhyAnswerId)).ToListAsync(cancellationToken);
    }

    private async Task<List<MorningBriefCitation>> CitationsForBriefsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var ids = await _db.MorningBriefs.IgnoreQueryFilters().Where(item => item.OrganizationId == organizationId).Select(item => item.Id).ToListAsync(cancellationToken);
        return await _db.MorningBriefCitations.IgnoreQueryFilters().AsNoTracking().Where(item => ids.Contains(item.MorningBriefId)).ToListAsync(cancellationToken);
    }

    private async Task<CallThresholdSettingsDto> ThresholdsForAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var stored = await _db.CallThresholdSettings.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(item => item.OrganizationId == organizationId, cancellationToken);
        if (stored is not null)
        {
            return ToDto(stored, isCustom: true);
        }

        var platform = await _db.PlatformCallDefaults.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (platform is null)
        {
            return new CallThresholdSettingsDto(
                CallThresholdDefaults.StoppedMissedCycles,
                CallThresholdDefaults.DroppedPercent,
                CallThresholdDefaults.DroppedMonths,
                CallThresholdDefaults.MinimumInvoiceHistory,
                IsCustom: false);
        }

        return new CallThresholdSettingsDto(
            platform.StoppedMissedCycles,
            platform.DroppedPercent,
            platform.DroppedMonths,
            platform.MinimumInvoiceHistory,
            IsCustom: false);
    }

    private async Task EnsureAnotherOwnerAsync(Guid organizationId, Guid exceptMembershipId, CancellationToken cancellationToken)
    {
        var owners = await _db.Memberships.IgnoreQueryFilters().CountAsync(
            item => item.OrganizationId == organizationId && item.Role == MembershipRole.Owner && item.Id != exceptMembershipId,
            cancellationToken);
        if (owners == 0)
        {
            throw new InvalidOperationException("A business needs an owner. Add another owner first, or delete the business.");
        }
    }

    private async Task<Organization> RequireBusinessAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await _db.Organizations.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == organizationId, cancellationToken)
        ?? throw new InvalidOperationException("Business not found.");

    private void RequireAdmin()
    {
        if (!_admin.IsAdmin)
        {
            throw new UnauthorizedAccessException("Vhona admin only.");
        }

        if (_admin.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("Sign in to continue.");
        }
    }

    private void Audit(string action, Guid? organizationId, string? target, string summary)
    {
        _db.AdminAuditEntries.Add(new AdminAuditEntry
        {
            Id = Guid.NewGuid(),
            At = DateTime.UtcNow,
            ActorUserId = _admin.UserId,
            ActorEmail = _admin.Email,
            Action = action,
            OrganizationId = organizationId,
            Target = target,
            Summary = summary
        });
    }

    private async Task AuditAsync(string action, Guid? organizationId, string? target, string summary, CancellationToken cancellationToken)
    {
        using var scope = AdminDataAccess.Open(_admin.UserId);
        Audit(action, organizationId, target, summary);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateThresholds(CallThresholdSettingsUpdate update)
    {
        var errors = CallThresholdValidator.Validate(
            update.StoppedMissedCycles,
            update.DroppedPercent,
            update.DroppedMonths,
            update.MinimumInvoiceHistory);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }
    }

    private void ApplyThresholds(CallThresholdSettings stored, CallThresholdSettingsUpdate update)
    {
        stored.StoppedMissedCycles = update.StoppedMissedCycles;
        stored.DroppedPercent = update.DroppedPercent;
        stored.DroppedMonths = update.DroppedMonths;
        stored.MinimumInvoiceHistory = update.MinimumInvoiceHistory;
        stored.UpdatedAt = DateTime.UtcNow;
        stored.UpdatedByUserId = _admin.UserId;
    }

    private static CallThresholdSettingsDto ToDto(CallThresholdSettings stored, bool isCustom) =>
        new(stored.StoppedMissedCycles, stored.DroppedPercent, stored.DroppedMonths, stored.MinimumInvoiceHistory, isCustom);

    private static AdminInviteRow ToInvite(Invitation invite, string organizationName) =>
        new(invite.Id, invite.OrganizationId, organizationName, invite.Email, invite.Token, invite.ExpiresAt, invite.RevokedAt is not null);

    private static AdminImportSummary ToImport(ImportJob job) =>
        new(job.Id, job.OriginalFileName, job.Kind.ToString(), job.Status.ToString(), job.ImportedRowCount, job.CreatedAt, job.ByteSize);

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class NullEventLog : IEventLog
    {
        public static readonly NullEventLog Instance = new();
        public void Append(AnalyticsEvent evt) { }
        public IReadOnlyList<AnalyticsEvent> Read() => Array.Empty<AnalyticsEvent>();
    }
}
