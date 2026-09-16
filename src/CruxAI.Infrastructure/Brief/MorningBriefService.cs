using System.Text.Json;
using CruxAI.Core.Analytics;
using CruxAI.Core.Brief;
using CruxAI.Core.Entities;
using CruxAI.Core.Health;
using CruxAI.Core.Identity;
using CruxAI.Core.Time;
using CruxAI.Core.Why;
using CruxAI.Infrastructure.Data;
using CruxAI.Infrastructure.Health;
using CruxAI.Infrastructure.Why;
using Microsoft.EntityFrameworkCore;

namespace CruxAI.Infrastructure.Brief;

/// <summary>
/// On-demand morning brief generation. An Azure Function timer may call
/// <see cref="EnsureTodaysBriefAsync"/>; local demo does not need Functions.
/// </summary>
public sealed class MorningBriefService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly CruxDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly HealthKpiService _health;
    private readonly WhyService _why;
    private readonly IClock _clock;
    private readonly IAnalytics _analytics;

    public MorningBriefService(
        CruxDbContext db,
        ICurrentUser currentUser,
        HealthKpiService health,
        WhyService why,
        IClock clock,
        IAnalytics? analytics = null)
    {
        _db = db;
        _currentUser = currentUser;
        _health = health;
        _why = why;
        _clock = clock;
        _analytics = analytics ?? NullAnalytics.Instance;
    }

    public async Task<MorningBriefPreferenceState> GetPreferenceAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var row = await LoadPreferenceAsync(cancellationToken);
        var trusted = await HasTrustedWhyAsync(cancellationToken);
        return ToState(row, trusted);
    }

    public async Task<MorningBriefOptInResult> OptInAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var row = await LoadOrCreatePreferenceAsync(cancellationToken);
        var trusted = await HasTrustedWhyAsync(cancellationToken);

        if (row.Dismissed)
        {
            return new MorningBriefOptInResult
            {
                Succeeded = false,
                Preference = ToState(row, trusted),
                Error = MorningBriefMessages.AlreadyDismissed
            };
        }

        if (!trusted)
        {
            return new MorningBriefOptInResult
            {
                Succeeded = false,
                Preference = ToState(row, trusted),
                Error = MorningBriefMessages.OptInRequired
            };
        }

        if (!row.OptedIn)
        {
            row.OptedIn = true;
            row.OptedInAt = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        _analytics.MarkOptedInThisSession();
        return new MorningBriefOptInResult
        {
            Succeeded = true,
            Preference = ToState(row, trusted)
        };
    }

    public async Task<MorningBriefPreferenceState> DismissAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var row = await LoadOrCreatePreferenceAsync(cancellationToken);
        if (!row.OptedIn && !row.Dismissed)
        {
            row.Dismissed = true;
            row.DismissedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        var trusted = await HasTrustedWhyAsync(cancellationToken);
        return ToState(row, trusted);
    }

    public async Task<MorningBriefLanding> GetLandingAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var preference = await GetPreferenceAsync(cancellationToken);
        if (!preference.OptedIn)
        {
            return MorningBriefLanding.NotOptedIn();
        }

        var landing = await EnsureTodaysBriefAsync(cancellationToken);
        _analytics.TrackBriefReturnIfEligible(preference.OptedInAt);
        return landing;
    }

    /// <summary>
    /// Generates today's brief on first next-visit read. Safe to call from a timer stub.
    /// </summary>
    public async Task<MorningBriefLanding> EnsureTodaysBriefAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var preference = await GetPreferenceAsync(cancellationToken);
        if (!preference.OptedIn)
        {
            return MorningBriefLanding.NotOptedIn();
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var existing = await _db.MorningBriefs
            .AsNoTracking()
            .Include(brief => brief.Citations)
            .FirstOrDefaultAsync(
                brief => brief.OrganizationId == _currentUser.OrganizationId
                         && brief.UserId == _currentUser.UserId
                         && brief.BriefDate == today,
                cancellationToken);

        if (existing is not null)
        {
            return await ToLandingAsync(existing, cancellationToken);
        }

        return await GenerateAndPersistAsync(today, cancellationToken);
    }

    private async Task<MorningBriefLanding> GenerateAndPersistAsync(
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var snapshot = await _health.GetSnapshotAsync(cancellationToken);
        var captured = MorningBriefComposer.Capture(snapshot);
        var transactions = await LoadTransactionsAsync(cancellationToken);
        var story = MorningBriefComposer.ComposeSingleStory(snapshot, transactions);

        if (!story.Verified || story.Citations.Count == 0)
        {
            var empty = new MorningBrief
            {
                Id = Guid.NewGuid(),
                OrganizationId = _currentUser.OrganizationId,
                UserId = _currentUser.UserId,
                BriefDate = today,
                SnapshotJson = Serialize(captured),
                CurrentPeriodLabel = captured.CurrentPeriodLabel,
                PreviousPeriodLabel = captured.PreviousPeriodLabel,
                Explanation = string.Empty,
                Verified = false,
                Metric = null,
                WhyAnswerId = null,
                CreatedAt = _clock.UtcNow
            };
            _db.MorningBriefs.Add(empty);
            await _db.SaveChangesAsync(cancellationToken);
            return MorningBriefLanding.FailClosed(empty.Id, today, captured);
        }

        var whyLog = new WhyAnswer
        {
            Id = Guid.NewGuid(),
            OrganizationId = _currentUser.OrganizationId,
            AskedByUserId = _currentUser.UserId,
            Question = story.Question,
            Answer = story.Answer,
            Verified = true,
            Metric = story.Metric?.ToString(),
            CreatedAt = _clock.UtcNow,
            Citations = story.Citations.Select(citation => new WhyCitation
            {
                Id = Guid.NewGuid(),
                RowId = citation.RowId,
                Columns = citation.Columns,
                PeriodLabel = citation.PeriodLabel
            }).ToList()
        };

        var brief = new MorningBrief
        {
            Id = Guid.NewGuid(),
            OrganizationId = _currentUser.OrganizationId,
            UserId = _currentUser.UserId,
            BriefDate = today,
            SnapshotJson = Serialize(captured),
            CurrentPeriodLabel = captured.CurrentPeriodLabel,
            PreviousPeriodLabel = captured.PreviousPeriodLabel,
            Explanation = story.Answer,
            Verified = true,
            Metric = story.Metric?.ToString(),
            WhyAnswerId = whyLog.Id,
            CreatedAt = _clock.UtcNow,
            Citations = story.Citations.Select(citation => new MorningBriefCitation
            {
                Id = Guid.NewGuid(),
                RowId = citation.RowId,
                Columns = citation.Columns,
                PeriodLabel = citation.PeriodLabel
            }).ToList()
        };

        _db.WhyAnswers.Add(whyLog);
        _db.MorningBriefs.Add(brief);
        await _db.SaveChangesAsync(cancellationToken);

        var loaded = await _db.MorningBriefs
            .AsNoTracking()
            .Include(item => item.Citations)
            .SingleAsync(item => item.Id == brief.Id, cancellationToken);
        return await ToLandingAsync(loaded, cancellationToken);
    }

    private async Task<MorningBriefLanding> ToLandingAsync(
        MorningBrief brief,
        CancellationToken cancellationToken)
    {
        var snapshot = Deserialize(brief.SnapshotJson) ?? new MorningBriefSnapshot
        {
            CurrentPeriodLabel = brief.CurrentPeriodLabel ?? string.Empty,
            PreviousPeriodLabel = brief.PreviousPeriodLabel ?? string.Empty
        };

        if (!brief.Verified || brief.Citations.Count == 0)
        {
            return MorningBriefLanding.FailClosed(brief.Id, brief.BriefDate, snapshot);
        }

        WhyAskResult? receipt = null;
        if (brief.WhyAnswerId is Guid answerId)
        {
            receipt = await _why.GetAsync(answerId, cancellationToken);
        }

        receipt ??= BuildReceiptFromBrief(brief, await LoadTransactionsByRowIdsAsync(
            brief.Citations.Select(citation => citation.RowId).ToList(),
            cancellationToken));

        if (receipt.CitedRows.Count == 0)
        {
            return MorningBriefLanding.FailClosed(brief.Id, brief.BriefDate, snapshot);
        }

        return new MorningBriefLanding
        {
            OptedIn = true,
            Verified = true,
            BriefId = brief.Id,
            BriefDate = brief.BriefDate,
            Snapshot = snapshot,
            Explanation = brief.Explanation,
            Metric = Enum.TryParse<HealthMetricKind>(brief.Metric, ignoreCase: true, out var metric) ? metric : null,
            Receipt = receipt
        };
    }

    private static WhyAskResult BuildReceiptFromBrief(
        MorningBrief brief,
        IReadOnlyList<Transaction> transactions)
    {
        var byId = transactions
            .GroupBy(t => t.RowId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var rows = new List<WhyCitedRow>();
        foreach (var citation in brief.Citations)
        {
            if (!byId.TryGetValue(citation.RowId, out var txn))
            {
                continue;
            }

            rows.Add(new WhyCitedRow
            {
                RowId = txn.RowId,
                Columns = citation.Columns,
                PeriodLabel = citation.PeriodLabel,
                Date = txn.Date,
                Description = txn.Description,
                Amount = txn.Amount,
                Category = txn.Category,
                Counterparty = txn.Counterparty,
                Reference = txn.Reference,
                Balance = txn.Balance
            });
        }

        return new WhyAskResult
        {
            AnswerId = brief.WhyAnswerId ?? brief.Id,
            Question = MorningBriefMessages.StoryQuestion,
            Answer = brief.Explanation,
            Verified = brief.Verified && rows.Count > 0,
            Metric = Enum.TryParse<HealthMetricKind>(brief.Metric, ignoreCase: true, out var metric) ? metric : null,
            CreatedAt = brief.CreatedAt,
            CitedRows = rows
        };
    }

    private async Task<bool> HasTrustedWhyAsync(CancellationToken cancellationToken) =>
        await _db.WhyAnswers
            .AsNoTracking()
            .Where(answer => answer.OrganizationId == _currentUser.OrganizationId
                             && answer.AskedByUserId == _currentUser.UserId
                             && answer.Verified)
            .AnyAsync(answer => answer.Citations.Any(), cancellationToken);

    private async Task<MorningBriefPreference?> LoadPreferenceAsync(CancellationToken cancellationToken) =>
        await _db.MorningBriefPreferences
            .FirstOrDefaultAsync(
                row => row.OrganizationId == _currentUser.OrganizationId && row.UserId == _currentUser.UserId,
                cancellationToken);

    private async Task<MorningBriefPreference> LoadOrCreatePreferenceAsync(CancellationToken cancellationToken)
    {
        var row = await LoadPreferenceAsync(cancellationToken);
        if (row is not null)
        {
            return row;
        }

        row = new MorningBriefPreference
        {
            Id = Guid.NewGuid(),
            OrganizationId = _currentUser.OrganizationId,
            UserId = _currentUser.UserId
        };
        _db.MorningBriefPreferences.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return row;
    }

    private async Task<List<Transaction>> LoadTransactionsAsync(CancellationToken cancellationToken) =>
        await _db.Transactions
            .AsNoTracking()
            .Where(t => t.OrganizationId == _currentUser.OrganizationId)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.SourceRowNumber)
            .ToListAsync(cancellationToken);

    private async Task<List<Transaction>> LoadTransactionsByRowIdsAsync(
        IReadOnlyCollection<string> rowIds,
        CancellationToken cancellationToken)
    {
        if (rowIds.Count == 0)
        {
            return [];
        }

        return await _db.Transactions
            .AsNoTracking()
            .Where(t => t.OrganizationId == _currentUser.OrganizationId && rowIds.Contains(t.RowId))
            .ToListAsync(cancellationToken);
    }

    private static MorningBriefPreferenceState ToState(MorningBriefPreference? row, bool trusted) =>
        new()
        {
            HasTrustedWhy = trusted,
            OptedIn = row?.OptedIn == true,
            Dismissed = row?.Dismissed == true,
            OptedInAt = row?.OptedInAt
        };

    private static string Serialize(MorningBriefSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);

    private static MorningBriefSnapshot? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<MorningBriefSnapshot>(json, JsonOptions);
    }

    private void EnsureAuthenticated()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new InvalidOperationException("Sign in to continue.");
        }
    }
}

/// <summary>
/// Placeholder for a future Azure Functions timer. Local demo generates on next visit instead.
/// </summary>
public static class MorningBriefFunctionsStub
{
    public const string Note =
        "Local demo generates briefs on-demand at next visit. Azure Functions are not required.";

    public static Task<MorningBriefLanding> RunTimerTriggerAsync(
        MorningBriefService briefs,
        CancellationToken cancellationToken = default) =>
        briefs.EnsureTodaysBriefAsync(cancellationToken);
}
