using CruxAI.Core.Analytics;
using CruxAI.Core.Entities;
using CruxAI.Core.Health;
using CruxAI.Core.Identity;
using CruxAI.Core.Why;
using CruxAI.Infrastructure.Data;
using CruxAI.Infrastructure.Health;
using Microsoft.EntityFrameworkCore;

namespace CruxAI.Infrastructure.Why;

public sealed class WhyService
{
    private readonly CruxDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly HealthKpiService _health;
    private readonly IAzureOpenAIIntentClassifier _azure;
    private readonly IAnalytics _analytics;

    public WhyService(
        CruxDbContext db,
        ICurrentUser currentUser,
        HealthKpiService health,
        IAzureOpenAIIntentClassifier azure,
        IAnalytics? analytics = null)
    {
        _db = db;
        _currentUser = currentUser;
        _health = health;
        _azure = azure;
        _analytics = analytics ?? NullAnalytics.Instance;
    }

    public async Task<WhyAskResult> AskAsync(
        string question,
        HealthMetricKind? seededMetric = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        question = question?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(question))
        {
            return UnloggedFailClosed(question);
        }

        var snapshot = await _health.GetSnapshotAsync(cancellationToken);
        var transactions = await LoadTransactionsAsync(cancellationToken);

        var intent = WhyIntentParser.Parse(question, seededMetric);
        if (intent.Metric is null)
        {
            var classified = await _azure.TryClassifyAsync(question, cancellationToken);
            if (classified is not null)
            {
                intent = new WhyIntent { Metric = classified, WantsChange = intent.WantsChange };
            }
        }

        var verification = WhyVerifier.Verify(question, intent, transactions, snapshot);
        if (verification.Verified && verification.Citations.Count == 0)
        {
            verification = WhyVerification.FailClosed(question);
        }

        var log = new WhyAnswer
        {
            Id = Guid.NewGuid(),
            OrganizationId = _currentUser.OrganizationId,
            AskedByUserId = _currentUser.UserId,
            Question = question,
            Answer = verification.Answer,
            Verified = verification.Verified,
            Metric = verification.Metric?.ToString(),
            CreatedAt = DateTime.UtcNow,
            Citations = verification.Citations.Select(citation => new WhyCitation
            {
                Id = Guid.NewGuid(),
                RowId = citation.RowId,
                Columns = citation.Columns,
                PeriodLabel = citation.PeriodLabel
            }).ToList()
        };

        _db.WhyAnswers.Add(log);
        await _db.SaveChangesAsync(cancellationToken);
        _analytics.TrackAsksWhySessionOne();

        return ToResult(log, AttachRows(log.Citations, transactions));
    }

    public async Task<WhyAskResult?> GetAsync(Guid answerId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var log = await _db.WhyAnswers
            .AsNoTracking()
            .Include(answer => answer.Citations)
            .FirstOrDefaultAsync(
                answer => answer.Id == answerId && answer.OrganizationId == _currentUser.OrganizationId,
                cancellationToken);

        if (log is null)
        {
            return null;
        }

        var transactions = await LoadTransactionsByRowIdsAsync(
            log.Citations.Select(citation => citation.RowId).ToList(),
            cancellationToken);

        return ToResult(log, AttachRows(log.Citations, transactions));
    }

    public async Task<IReadOnlyList<WhyCitedRow>> GetReceiptRowsAsync(
        Guid answerId,
        CancellationToken cancellationToken = default)
    {
        var result = await GetAsync(answerId, cancellationToken);
        return result?.CitedRows ?? [];
    }

    public async Task<IReadOnlyList<WhyAnswer>> ListRecentAsync(
        int take = 8,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        return await _db.WhyAnswers
            .AsNoTracking()
            .Where(answer => answer.OrganizationId == _currentUser.OrganizationId)
            .OrderByDescending(answer => answer.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
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

    private static IReadOnlyList<WhyCitedRow> AttachRows(
        IEnumerable<WhyCitation> citations,
        IReadOnlyList<Transaction> transactions)
    {
        var byId = transactions
            .GroupBy(t => t.RowId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var rows = new List<WhyCitedRow>();
        foreach (var citation in citations)
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

        return rows;
    }

    private static WhyAskResult ToResult(WhyAnswer log, IReadOnlyList<WhyCitedRow> rows) =>
        new()
        {
            AnswerId = log.Id,
            Question = log.Question,
            Answer = log.Answer,
            Verified = log.Verified,
            Metric = Enum.TryParse<HealthMetricKind>(log.Metric, ignoreCase: true, out var metric) ? metric : null,
            CreatedAt = log.CreatedAt,
            CitedRows = rows
        };

    private static WhyAskResult UnloggedFailClosed(string question) =>
        new()
        {
            AnswerId = Guid.Empty,
            Question = question,
            Answer = WhyMessages.Unverified,
            Verified = false,
            CreatedAt = DateTime.UtcNow,
            CitedRows = []
        };

    private void EnsureAuthenticated()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new InvalidOperationException("Sign in to continue.");
        }
    }
}
