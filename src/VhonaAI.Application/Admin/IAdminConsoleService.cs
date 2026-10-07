using VhonaAI.Application.Calling;

namespace VhonaAI.Application.Admin;

public interface IAdminConsoleService
{
    Task<AdminOverview> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminBusinessSummary>> SearchBusinessesAsync(string? query, CancellationToken cancellationToken = default);

    Task<AdminBusinessDetail> GetBusinessAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task RenameBusinessAsync(Guid organizationId, string name, CancellationToken cancellationToken = default);

    Task SetBusinessDisabledAsync(Guid organizationId, bool disabled, CancellationToken cancellationToken = default);

    Task DeleteBusinessAsync(Guid organizationId, string typedName, CancellationToken cancellationToken = default);

    Task<AdminExportFile> ExportBusinessAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminUserSummary>> SearchUsersAsync(string? query, CancellationToken cancellationToken = default);

    Task<AdminUserDetail> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task SetUserRoleAsync(Guid membershipId, string role, CancellationToken cancellationToken = default);

    Task RemoveMemberAsync(Guid membershipId, CancellationToken cancellationToken = default);

    Task<AdminInviteIssued> ResendInviteAsync(Guid invitationId, CancellationToken cancellationToken = default);

    Task RevokeInviteAsync(Guid invitationId, CancellationToken cancellationToken = default);

    Task SetUserDisabledAsync(Guid userId, bool disabled, CancellationToken cancellationToken = default);

    Task<AdminImportDetail> GetImportAsync(Guid importId, CancellationToken cancellationToken = default);

    Task DeleteImportAsync(Guid importId, CancellationToken cancellationToken = default);

    Task<CallThresholdSettingsDto> UpdateThresholdsAsync(
        Guid organizationId,
        CallThresholdSettingsUpdate update,
        CancellationToken cancellationToken = default);

    Task<CallThresholdSettingsDto> GetGlobalDefaultsAsync(CancellationToken cancellationToken = default);

    Task<CallThresholdSettingsDto> UpdateGlobalDefaultsAsync(
        CallThresholdSettingsUpdate update,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminPartnerEventRow>> ListPartnerEventsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminAuditRow>> ListAuditAsync(int take = 100, CancellationToken cancellationToken = default);
}

public sealed record AdminOverview(int BusinessCount, int UserCount, int DisabledBusinessCount, int PendingInviteCount);

public sealed record AdminBusinessSummary(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    bool IsDisabled,
    string? OwnerEmail,
    int MemberCount,
    int InvoiceCount,
    int TransactionCount,
    DateTime? LastImportAt);

public sealed record AdminMemberRow(
    Guid MembershipId,
    Guid UserId,
    string Email,
    string DisplayName,
    string Role,
    bool UserDisabled);

public sealed record AdminInviteRow(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string Email,
    string Token,
    DateTime ExpiresAt,
    bool IsRevoked);

public sealed record AdminDataSourceRow(Guid Id, string Name, string Kind, DateTime CreatedAt);

public sealed record AdminImportSummary(
    Guid Id,
    string FileName,
    string Kind,
    string Status,
    int? ImportedRowCount,
    DateTime CreatedAt,
    long ByteSize);

public sealed record AdminBusinessDetail(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    bool IsDisabled,
    int CustomerCount,
    int InvoiceCount,
    int TransactionCount,
    IReadOnlyList<AdminMemberRow> Members,
    IReadOnlyList<AdminInviteRow> Invites,
    IReadOnlyList<AdminDataSourceRow> DataSources,
    IReadOnlyList<AdminImportSummary> Imports,
    CallThresholdSettingsDto Thresholds);

public sealed record AdminUserSummary(Guid Id, string Email, string DisplayName, bool IsDisabled, string Businesses);

public sealed record AdminUserMembership(Guid MembershipId, Guid OrganizationId, string OrganizationName, string Role);

public sealed record AdminUserDetail(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsDisabled,
    string? ExternalId,
    IReadOnlyList<AdminUserMembership> Memberships,
    IReadOnlyList<AdminInviteRow> Invites);

public sealed record AdminTransactionRow(
    string RowId,
    DateOnly Date,
    DateOnly? BookedDate,
    string Description,
    decimal Amount,
    string? Counterparty);

public sealed record AdminInvoiceRow(
    string RowId,
    string Number,
    string Customer,
    DateOnly InvoiceDate,
    decimal Amount,
    decimal AmountDue,
    string Status);

public sealed record AdminImportDetail(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string FileName,
    string Status,
    bool Truncated,
    IReadOnlyList<AdminTransactionRow> Transactions,
    IReadOnlyList<AdminInvoiceRow> Invoices);

public sealed record AdminExportFile(byte[] Content, string FileName);

public sealed record AdminPartnerEventRow(string Name, Guid OrgId, Guid UserId, DateTime Timestamp);

public sealed record AdminAuditRow(
    Guid Id,
    DateTime At,
    string ActorEmail,
    string Action,
    Guid? OrganizationId,
    string? Target,
    string Summary);

public sealed record AdminInviteIssued(Guid Id, string Email, string Token, DateTime ExpiresAt);
