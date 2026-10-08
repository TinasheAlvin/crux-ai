using VhonaAI.Core.Csv;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Validation;

namespace VhonaAI.Application.Imports;

public interface IImportAppService
{
    Task<ImportJob> CreateFromUploadAsync(
        Stream content,
        string originalFileName,
        long byteSize,
        ImportKind kind = ImportKind.Transactions,
        CancellationToken cancellationToken = default);

    Task<ImportJob> GetJobAsync(Guid importJobId, CancellationToken cancellationToken = default);
    Task<CsvTable> LoadTableAsync(ImportJob job, CancellationToken cancellationToken = default);
    IReadOnlyDictionary<string, string> GetMapping(ImportJob job, CsvTable table);
    IReadOnlyList<string> SampleValues(CsvTable table, string header, int take = 3);

    Task SaveMappingAsync(
        ImportJob job,
        IReadOnlyDictionary<string, string> mapping,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ValidatedImportRow>> ValidateAsync(ImportJob job, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ValidatedInvoiceRow>> ValidateInvoicesAsync(ImportJob job, CancellationToken cancellationToken = default);

    Task SaveCorrectionsAsync(
        ImportJob job,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> corrections,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Transaction>> PersistAsync(
        ImportJob job,
        bool includeOnlyValid,
        CancellationToken cancellationToken = default);

    Task<InvoiceImportResult> PersistInvoicesAsync(
        ImportJob job,
        bool includeOnlyValid,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportJob>> ListRecentAsync(int take = 10, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Transaction>> ListTransactionsAsync(Guid importJobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Invoice>> ListInvoicesAsync(Guid importJobId, CancellationToken cancellationToken = default);
    Task<int> CountTransactionsAsync(CancellationToken cancellationToken = default);
}

public sealed class InvoiceImportResult
{
    public required int CustomerCount { get; init; }
    public required int InvoiceCount { get; init; }
    public required int LineCount { get; init; }
    public required int PaymentCount { get; init; }
    public required int CreditNoteCount { get; init; }
    public required IReadOnlyList<Invoice> Invoices { get; init; }
}
