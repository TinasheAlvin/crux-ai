using System.Text.Json;
using VhonaAI.Core.Analytics;
using VhonaAI.Core.Csv;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Identity;
using VhonaAI.Core.Mapping;
using VhonaAI.Core.Parsing;
using VhonaAI.Core.Storage;
using VhonaAI.Core.Validation;
using VhonaAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Infrastructure.Imports;

public sealed class ImportService
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly VhonaDbContext _db;
    private readonly IFileStorage _storage;
    private readonly ICsvReader _csvReader;
    private readonly ICurrentUser _currentUser;
    private readonly IAnalytics _analytics;

    public ImportService(
        VhonaDbContext db,
        IFileStorage storage,
        ICsvReader csvReader,
        ICurrentUser currentUser,
        IAnalytics? analytics = null)
    {
        _db = db;
        _storage = storage;
        _csvReader = csvReader;
        _currentUser = currentUser;
        _analytics = analytics ?? NullAnalytics.Instance;
    }

    public async Task<ImportJob> CreateFromUploadAsync(
        Stream content,
        string originalFileName,
        long byteSize,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        ValidateUpload(originalFileName, byteSize);

        var job = new ImportJob
        {
            Id = Guid.NewGuid(),
            OrganizationId = _currentUser.OrganizationId,
            CreatedByUserId = _currentUser.UserId,
            OriginalFileName = Path.GetFileName(originalFileName),
            ByteSize = byteSize,
            Status = ImportStatus.Uploaded,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        job.StoragePath = await _storage.SaveAsync(
            content,
            job.OrganizationId,
            job.Id,
            job.OriginalFileName,
            cancellationToken);

        // Confirm the file is a readable CSV before we keep the job.
        await using (var stored = await _storage.OpenReadAsync(job.StoragePath, cancellationToken))
        {
            _ = await _csvReader.ReadAsync(stored, cancellationToken);
        }

        var savedProfile = await GetDefaultProfileAsync(job.OrganizationId, cancellationToken);
        var table = await LoadTableAsync(job, cancellationToken);
        var guessed = ColumnGuesser.Guess(table.Headers);
        var savedMapping = DeserializeMapping(savedProfile?.MappingJson);
        var mapping = ColumnGuesser.MergeSaved(table.Headers, guessed, savedMapping);
        job.MappingJson = JsonSerializer.Serialize(mapping);

        _db.ImportJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);
        return job;
    }

    public async Task<ImportJob> GetJobAsync(Guid importJobId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var job = await _db.ImportJobs
            .FirstOrDefaultAsync(
                x => x.Id == importJobId && x.OrganizationId == _currentUser.OrganizationId,
                cancellationToken);

        return job ?? throw new InvalidOperationException("Import not found for this organisation.");
    }

    public async Task<CsvTable> LoadTableAsync(ImportJob job, CancellationToken cancellationToken = default)
    {
        await using var stream = await _storage.OpenReadAsync(job.StoragePath, cancellationToken);
        return await _csvReader.ReadAsync(stream, cancellationToken);
    }

    public IReadOnlyDictionary<string, string> GetMapping(ImportJob job, CsvTable table)
    {
        var stored = DeserializeMapping(job.MappingJson);
        if (stored.Count > 0)
        {
            return ColumnGuesser.MergeSaved(table.Headers, ColumnGuesser.Guess(table.Headers), stored);
        }

        return ColumnGuesser.Guess(table.Headers);
    }

    public IReadOnlyList<string> SampleValues(CsvTable table, string header, int take = 3) =>
        table.Rows
            .Select(row => row.Values.TryGetValue(header, out var value) ? value : string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(take)
            .ToList();

    public async Task SaveMappingAsync(
        ImportJob job,
        IReadOnlyDictionary<string, string> mapping,
        CancellationToken cancellationToken = default)
    {
        var errors = MappingValidator.Validate(mapping);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        job.MappingJson = JsonSerializer.Serialize(mapping);
        job.Status = ImportStatus.Mapped;
        job.UpdatedAt = DateTime.UtcNow;

        var profile = await GetDefaultProfileAsync(job.OrganizationId, cancellationToken);
        if (profile is null)
        {
            profile = new ColumnMappingProfile
            {
                Id = Guid.NewGuid(),
                OrganizationId = job.OrganizationId,
                Name = "Default",
                UpdatedAt = DateTime.UtcNow
            };
            _db.ColumnMappingProfiles.Add(profile);
        }

        profile.MappingJson = job.MappingJson;
        profile.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ValidatedImportRow>> ValidateAsync(
        ImportJob job,
        CancellationToken cancellationToken = default)
    {
        var table = await LoadTableAsync(job, cancellationToken);
        var mapping = GetMapping(job, table);
        var corrections = DeserializeCorrections(job.CorrectionsJson);
        return ImportRowValidator.Validate(table, mapping, corrections);
    }

    public async Task SaveCorrectionsAsync(
        ImportJob job,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> corrections,
        CancellationToken cancellationToken = default)
    {
        job.CorrectionsJson = JsonSerializer.Serialize(corrections);
        job.Status = ImportStatus.Validated;
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Transaction>> PersistAsync(
        ImportJob job,
        bool includeOnlyValid,
        CancellationToken cancellationToken = default)
    {
        var rows = await ValidateAsync(job, cancellationToken);
        var valid = rows.Where(row => row.IsValid).ToList();
        var invalidCount = rows.Count - valid.Count;

        if (valid.Count == 0)
        {
            throw new InvalidOperationException("No valid rows to import. Fix the highlighted errors and re-validate.");
        }

        if (!includeOnlyValid && invalidCount > 0)
        {
            throw new InvalidOperationException(
                $"{invalidCount} row(s) still have errors. Fix them here, or import only the valid rows.");
        }

        var now = DateTime.UtcNow;
        var existing = await _db.Transactions
            .Where(t => t.ImportJobId == job.Id)
            .ToDictionaryAsync(t => t.RowId, cancellationToken);

        var persisted = new List<Transaction>();
        foreach (var row in valid)
        {
            var parsed = row.Parsed!;
            var rowId = RowIdFactory.ForImportLine(job.Id, parsed.SourceRowNumber);
            if (!existing.TryGetValue(rowId, out var transaction))
            {
                transaction = new Transaction
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = job.OrganizationId,
                    ImportJobId = job.Id,
                    RowId = rowId
                };
                _db.Transactions.Add(transaction);
            }

            transaction.SourceRowNumber = parsed.SourceRowNumber;
            transaction.Date = parsed.Date;
            transaction.Description = parsed.Description;
            transaction.Amount = parsed.Amount;
            transaction.Currency = "ZAR";
            transaction.Category = parsed.Category;
            transaction.Reference = parsed.Reference;
            transaction.Counterparty = parsed.Counterparty;
            transaction.Balance = parsed.Balance;
            transaction.ImportedAt = now;
            persisted.Add(transaction);
        }

        job.Status = ImportStatus.Imported;
        job.ImportedRowCount = persisted.Count;
        job.UpdatedAt = now;
        job.ErrorSummary = invalidCount > 0
            ? $"{invalidCount} invalid row(s) skipped."
            : null;

        await _db.SaveChangesAsync(cancellationToken);
        _analytics.TrackFinishesUpload(job.Id, persisted.Count);
        return persisted.OrderBy(t => t.SourceRowNumber).ToList();
    }

    public async Task<IReadOnlyList<ImportJob>> ListRecentAsync(int take = 10, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        return await _db.ImportJobs
            .Where(j => j.OrganizationId == _currentUser.OrganizationId)
            .OrderByDescending(j => j.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Transaction>> ListTransactionsAsync(
        Guid importJobId,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        return await _db.Transactions
            .Where(t => t.OrganizationId == _currentUser.OrganizationId && t.ImportJobId == importJobId)
            .OrderBy(t => t.SourceRowNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountTransactionsAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        return await _db.Transactions
            .CountAsync(t => t.OrganizationId == _currentUser.OrganizationId, cancellationToken);
    }

    private async Task<ColumnMappingProfile?> GetDefaultProfileAsync(
        Guid organizationId,
        CancellationToken cancellationToken) =>
        await _db.ColumnMappingProfiles
            .FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.Name == "Default", cancellationToken);

    private static IReadOnlyDictionary<string, string> DeserializeMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
               ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> DeserializeCorrections(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<int, IReadOnlyDictionary<string, string>>();
        }

        var raw = JsonSerializer.Deserialize<Dictionary<int, Dictionary<string, string>>>(json, JsonOptions)
                  ?? new Dictionary<int, Dictionary<string, string>>();

        return raw.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, string>)pair.Value);
    }

    private static void ValidateUpload(string originalFileName, long byteSize)
    {
        if (byteSize <= 0)
        {
            throw new InvalidOperationException("The uploaded file is empty.");
        }

        if (byteSize > MaxUploadBytes)
        {
            throw new InvalidOperationException("CSV files larger than 10 MB are not supported in this slice.");
        }

        var extension = Path.GetExtension(originalFileName);
        if (!string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Upload a .csv file.");
        }
    }

    private void EnsureAuthenticated()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw new InvalidOperationException("Sign in to continue.");
        }
    }
}
