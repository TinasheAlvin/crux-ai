using VhonaAI.Core.Calling;

namespace VhonaAI.Application.Calling;

public interface IWhoToCallAppService
{
    Task<WhoToCallList> GetListAsync(CancellationToken cancellationToken = default);

    Task<WhoToCallFlagDetail?> GetFlagAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<ReminderDraftDto> SaveDraftAsync(Guid customerId, string body, CancellationToken cancellationToken = default);

    Task RecordActionAsync(
        Guid customerId,
        CallActionKind kind,
        DateOnly? snoozeUntil,
        string? note,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CallActionDto>> GetHistoryAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task LoadSampleAsync(string bookId, CancellationToken cancellationToken = default);
}

public sealed class WhoToCallList
{
    public DateOnly AsAt { get; init; }
    public string BusinessName { get; init; } = string.Empty;
    public string SortExplanation { get; init; } = WhoToCallResult.SortExplanation;
    public string ReceiptFooter { get; init; } = WhoToCallResult.ReceiptFooter;
    public bool CanLoadSample { get; init; }
    public bool UsingSample { get; init; }
    public string? SampleName { get; init; }
    public bool IsOwner { get; init; }
    public bool HasInvoices { get; init; }
    public IReadOnlyList<WhoToCallFlag> Flags { get; init; } = [];
}

public sealed class WhoToCallFlagDetail
{
    public required WhoToCallFlag Flag { get; init; }
    public DateOnly AsAt { get; init; }
    public string BusinessName { get; init; } = string.Empty;
    public string ReceiptFooter { get; init; } = WhoToCallResult.ReceiptFooter;
    public string? DraftBody { get; init; }
    public string DraftStatus { get; init; } = ReminderDraftTemplate.NotSent;
    public string? WhatsAppLink { get; init; }
    public string? EmailLink { get; init; }
    public bool CanEdit { get; init; }
    public IReadOnlyList<CallActionDto> History { get; init; } = [];
}

public sealed record CallActionDto(
    CallActionKind Kind,
    DateTime At,
    DateOnly? SnoozeUntil,
    string? Note,
    string ActorName);

public sealed record ReminderDraftDto(Guid CustomerId, string Body, string Status);
