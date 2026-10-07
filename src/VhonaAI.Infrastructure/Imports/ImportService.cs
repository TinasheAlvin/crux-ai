using System.Text.Json;
using VhonaAI.Application.Imports;
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

public sealed class ImportService : IImportAppService
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
        ImportKind kind = ImportKind.Transactions,
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
            Kind = kind,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        job.StoragePath = await _storage.SaveAsync(
            content,
            job.OrganizationId,
            job.Id,
            job.OriginalFileName,
            cancellationToken);

        // Confirm the file can be read before we keep the job.
        await using (var stored = await _storage.OpenReadAsync(job.StoragePath, cancellationToken))
        {
            _ = await ReadTableAsync(stored, job.OriginalFileName, cancellationToken);
        }

        var savedProfile = await GetProfileAsync(job, cancellationToken);
        var table = await LoadTableAsync(job, cancellationToken);
        var mapping = GetMapping(job, table, DeserializeMapping(savedProfile?.MappingJson));
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
        return await ReadTableAsync(stream, job.OriginalFileName, cancellationToken);
    }

    public IReadOnlyDictionary<string, string> GetMapping(ImportJob job, CsvTable table) =>
        GetMapping(job, table, DeserializeMapping(job.MappingJson));

    private IReadOnlyDictionary<string, string> GetMapping(
        ImportJob job,
        CsvTable table,
        IReadOnlyDictionary<string, string> stored)
    {
        if (job.Kind == ImportKind.Invoices)
        {
            var guessed = InvoiceColumnGuesser.Guess(table.Headers);
            return stored.Count > 0
                ? ColumnGuesser.MergeSaved(table.Headers, guessed, stored, InvoiceFields.All)
                : guessed;
        }

        var transactionGuess = ColumnGuesser.Guess(table.Headers);
        return stored.Count > 0
            ? ColumnGuesser.MergeSaved(table.Headers, transactionGuess, stored)
            : transactionGuess;
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
        var errors = job.Kind == ImportKind.Invoices
            ? InvoiceMappingValidator.Validate(mapping)
            : MappingValidator.Validate(mapping);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        job.MappingJson = JsonSerializer.Serialize(mapping);
        job.Status = ImportStatus.Mapped;
        job.UpdatedAt = DateTime.UtcNow;

        var profileName = ProfileName(job.Kind);
        var profile = await GetProfileAsync(job, cancellationToken);
        if (profile is null)
        {
            profile = new ColumnMappingProfile
            {
                Id = Guid.NewGuid(),
                OrganizationId = job.OrganizationId,
                Name = profileName,
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
        if (job.Kind == ImportKind.Invoices)
        {
            throw new InvalidOperationException("This file is an invoice import. Validate it as invoices.");
        }

        var table = await LoadTableAsync(job, cancellationToken);
        var mapping = GetMapping(job, table);
        var corrections = DeserializeCorrections(job.CorrectionsJson);
        return ImportRowValidator.Validate(table, mapping, corrections);
    }

    public async Task<IReadOnlyList<ValidatedInvoiceRow>> ValidateInvoicesAsync(
        ImportJob job,
        CancellationToken cancellationToken = default)
    {
        if (job.Kind != ImportKind.Invoices)
        {
            throw new InvalidOperationException("This file is a transaction import.");
        }

        var table = await LoadTableAsync(job, cancellationToken);
        var mapping = GetMapping(job, table);
        var corrections = DeserializeCorrections(job.CorrectionsJson);
        return InvoiceRowValidator.Validate(table, mapping, corrections);
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
        if (job.Kind == ImportKind.Invoices)
        {
            throw new InvalidOperationException("This file is an invoice import.");
        }

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
        var excel = job.OriginalFileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
        var source = await ImportSourceFactory.GetOrCreateAsync(
            _db,
            job,
            excel ? DataSourceKind.TransactionExcel : DataSourceKind.TransactionCsv,
            excel ? "xlsx" : "csv",
            cancellationToken);
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
            transaction.BookedDate = parsed.BookedDate;
            transaction.DataSourceId = source.Id;
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

    public async Task<InvoiceImportResult> PersistInvoicesAsync(
        ImportJob job,
        bool includeOnlyValid,
        CancellationToken cancellationToken = default)
    {
        var rows = await ValidateInvoicesAsync(job, cancellationToken);
        var result = await new InvoiceImportPersister(_db).PersistAsync(job, rows, includeOnlyValid, cancellationToken);
        _analytics.TrackFinishesUpload(job.Id, result.InvoiceCount + result.PaymentCount + result.CreditNoteCount);
        return result;
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

    public async Task<IReadOnlyList<Invoice>> ListInvoicesAsync(
        Guid importJobId,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        return await _db.Invoices
            .Include(invoice => invoice.Customer)
            .Where(invoice => invoice.OrganizationId == _currentUser.OrganizationId && invoice.ImportJobId == importJobId)
            .OrderBy(invoice => invoice.SourceRowNumber)
            .ToListAsync(cancellationToken);
    }

    private async Task<ColumnMappingProfile?> GetProfileAsync(ImportJob job, CancellationToken cancellationToken) =>
        await _db.ColumnMappingProfiles.FirstOrDefaultAsync(
            profile => profile.OrganizationId == job.OrganizationId && profile.Name == ProfileName(job.Kind),
            cancellationToken);

    private static string ProfileName(ImportKind kind) =>
        kind == ImportKind.Invoices ? "Invoices" : "Default";

    private async Task<CsvTable> ReadTableAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        if (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            return ExcelSheetReader.Read(buffer);
        }

        return await _csvReader.ReadAsync(stream, cancellationToken);
    }

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
            throw new InvalidOperationException("Files larger than 10 MB are not supported in this slice.");
        }

        var extension = Path.GetExtension(originalFileName);
        if (!string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Upload a .csv or .xlsx file.");
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
