using VhonaAI.Application.Calling;
using VhonaAI.Core.Calling;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Identity;
using VhonaAI.Core.Parsing;
using VhonaAI.Core.Time;
using VhonaAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Infrastructure.Calling;

public sealed class WhoToCallService : IWhoToCallAppService
{
    public const string SampleSourceName = "Who to call sample";
    private const int MaxDraftLength = 4000;
    private const int MaxNoteLength = 500;

    private readonly VhonaDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ICallSettingsAppService _settings;

    public WhoToCallService(
        VhonaDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        ICallSettingsAppService settings)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _settings = settings;
    }

    public async Task<WhoToCallList> GetListAsync(CancellationToken cancellationToken = default)
    {
        RequireMember();
        var computed = await ComputeAsync(cancellationToken);
        var visible = await VisibleFlagsAsync(computed, cancellationToken);
        var invoiceCount = await _db.Invoices.CountAsync(cancellationToken);
        var sample = await SampleSourceAsync(cancellationToken);
        return new WhoToCallList
        {
            AsAt = computed.Result.AsAt,
            BusinessName = computed.Result.BusinessName,
            SortExplanation = WhoToCallResult.SortExplanation,
            ReceiptFooter = WhoToCallResult.ReceiptFooter,
            CanLoadSample = _currentUser.IsOwner && invoiceCount == 0,
            UsingSample = sample.Using,
            SampleName = sample.Name,
            IsOwner = _currentUser.IsOwner,
            HasInvoices = invoiceCount > 0,
            Flags = visible
        };
    }

    public async Task<WhoToCallFlagDetail?> GetFlagAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        RequireMember();
        var computed = await ComputeAsync(cancellationToken);
        var visible = await VisibleFlagsAsync(computed, cancellationToken);
        var flag = visible.FirstOrDefault(item => item.CustomerId == customerId);
        if (flag is null)
        {
            return null;
        }

        var draft = await DraftBodyAsync(flag, cancellationToken);
        var history = await HistoryForAsync(customerId, cancellationToken);
        return new WhoToCallFlagDetail
        {
            Flag = flag,
            AsAt = computed.Result.AsAt,
            BusinessName = computed.Result.BusinessName,
            ReceiptFooter = WhoToCallResult.ReceiptFooter,
            DraftBody = draft,
            DraftStatus = ReminderDraftTemplate.NotSent,
            WhatsAppLink = draft is null ? null : ReminderHandoff.WhatsAppLink(flag.Phone, draft),
            EmailLink = draft is null ? null : ReminderHandoff.EmailLink(flag.Email, "Open invoices from " + computed.Result.BusinessName, draft),
            CanEdit = _currentUser.IsOwner && flag.Kind == CallFlagKind.Late,
            History = history
        };
    }

    public async Task<ReminderDraftDto> SaveDraftAsync(Guid customerId, string body, CancellationToken cancellationToken = default)
    {
        RequireOwner();
        var text = (body ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new InvalidOperationException("Write the reminder before saving it.");
        }

        if (text.Length > MaxDraftLength)
        {
            throw new InvalidOperationException($"A reminder can be at most {MaxDraftLength} characters.");
        }

        var computed = await ComputeAsync(cancellationToken);
        var visible = await VisibleFlagsAsync(computed, cancellationToken);
        var flag = visible.FirstOrDefault(item => item.CustomerId == customerId);
        if (flag is null || flag.Kind != CallFlagKind.Late)
        {
            throw new InvalidOperationException("Reminders are only drafted for a late customer who is still flagged.");
        }

        var stored = await _db.ReminderDrafts.FirstOrDefaultAsync(item => item.CustomerId == customerId, cancellationToken);
        if (stored is null)
        {
            stored = new ReminderDraft
            {
                Id = Guid.NewGuid(),
                OrganizationId = _currentUser.OrganizationId,
                CustomerId = customerId
            };
            _db.ReminderDrafts.Add(stored);
        }

        stored.Body = text;
        stored.EditedByOwner = true;
        stored.UpdatedAt = _clock.UtcNow;
        stored.Status = ReminderDraftTemplate.NotSent;
        await _db.SaveChangesAsync(cancellationToken);
        return new ReminderDraftDto(customerId, stored.Body, stored.Status);
    }

    public async Task RecordActionAsync(
        Guid customerId,
        CallActionKind kind,
        DateOnly? snoozeUntil,
        string? note,
        CancellationToken cancellationToken = default)
    {
        RequireOwner();
        if (!Enum.IsDefined(kind))
        {
            throw new InvalidOperationException("Choose called, snoozed, paid, or not a concern.");
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        if (kind == CallActionKind.Snoozed && snoozeUntil is null)
        {
            throw new InvalidOperationException("Choose the date to snooze until.");
        }

        if (kind == CallActionKind.Snoozed && snoozeUntil < today)
        {
            throw new InvalidOperationException("Choose today or a later date to snooze until.");
        }

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > MaxNoteLength })
        {
            throw new InvalidOperationException($"A note can be at most {MaxNoteLength} characters.");
        }

        var computed = await ComputeAsync(cancellationToken);
        var flag = computed.Result.Flags.FirstOrDefault(item => item.CustomerId == customerId);
        if (flag is null)
        {
            throw new InvalidOperationException("This customer is no longer flagged.");
        }

        var customerExists = await _db.Customers.AnyAsync(item => item.Id == customerId, cancellationToken);
        if (!customerExists)
        {
            throw new InvalidOperationException("This customer is no longer flagged.");
        }

        _db.CallCustomerActions.Add(new CallCustomerAction
        {
            Id = Guid.NewGuid(),
            OrganizationId = _currentUser.OrganizationId,
            CustomerId = customerId,
            Kind = kind,
            SnoozeUntil = kind == CallActionKind.Snoozed ? snoozeUntil : null,
            Note = trimmed,
            EvidenceKey = flag.EvidenceKey,
            ActedByUserId = _currentUser.UserId,
            At = _clock.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CallActionDto>> GetHistoryAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        RequireMember();
        var known = await _db.Customers.AnyAsync(item => item.Id == customerId, cancellationToken);
        if (!known)
        {
            return [];
        }

        return await HistoryForAsync(customerId, cancellationToken);
    }

    public async Task LoadSampleAsync(string bookId, CancellationToken cancellationToken = default)
    {
        RequireOwner();
        await EnsureEmptyAsync(cancellationToken);
        var ledger = SampleBookCatalog.Build(bookId);
        await PersistAsync(ledger.Customers, ledger.CreditNotes, ledger.Transactions, ledger.DisplayName, cancellationToken);
    }

    /// <summary>The original four customer fixture. Tests use this. The product offers Karoo and Axum instead.</summary>
    public async Task LoadLegacyFixtureAsync(CancellationToken cancellationToken = default)
    {
        RequireOwner();
        await EnsureEmptyAsync(cancellationToken);
        var books = WhoToCallSample.Build(_currentUser.OrganizationName);
        await PersistAsync(books.Customers, [], [], SampleSourceName, cancellationToken);
    }

    private async Task EnsureEmptyAsync(CancellationToken cancellationToken)
    {
        if (await _db.Invoices.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException("Sample books load only when this business has no invoices yet.");
        }
    }

    private async Task PersistAsync(
        IReadOnlyList<WhoToCallCustomerBook> customers,
        IReadOnlyList<SampleCreditNote> creditNotes,
        IReadOnlyList<SampleBankRow> transactions,
        string sourceName,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var orgId = _currentUser.OrganizationId;
        var hasBank = transactions.Count > 0;
        var invoiceJobId = Guid.NewGuid();
        var transactionJobId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        if (hasBank)
        {
            _db.ImportJobs.Add(new ImportJob
            {
                Id = invoiceJobId,
                OrganizationId = orgId,
                CreatedByUserId = _currentUser.UserId,
                OriginalFileName = sourceName + " invoices.csv",
                StoragePath = "sample/" + sourceName + "/invoices.csv",
                Status = ImportStatus.Imported,
                Kind = ImportKind.Invoices,
                ImportedRowCount = customers.Sum(customer => customer.Invoices.Count),
                CreatedAt = now,
                UpdatedAt = now
            });
            _db.ImportJobs.Add(new ImportJob
            {
                Id = transactionJobId,
                OrganizationId = orgId,
                CreatedByUserId = _currentUser.UserId,
                OriginalFileName = sourceName + " transactions.csv",
                StoragePath = "sample/" + sourceName + "/transactions.csv",
                Status = ImportStatus.Imported,
                Kind = ImportKind.Transactions,
                MappingJson = """{"Date":"Date","Description":"Description","Amount":"Amount","Balance":"Balance","BookedDate":"BookedDate","Category":"Category","Counterparty":"Counterparty"}""",
                ImportedRowCount = transactions.Count,
                CreatedAt = now,
                UpdatedAt = now.AddSeconds(1)
            });
        }

        _db.DataSources.Add(new DataSource
        {
            Id = sourceId,
            OrganizationId = orgId,
            Name = sourceName,
            Kind = DataSourceKind.InvoiceCsv,
            ExternalSystem = "sample",
            ImportJobId = hasBank ? invoiceJobId : null,
            CreatedAt = now
        });

        var invoiceIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var sourceRow = 1;
        foreach (var customer in customers)
        {
            _db.Customers.Add(new Customer
            {
                Id = customer.CustomerId,
                OrganizationId = orgId,
                DataSourceId = sourceId,
                RowId = customer.RowId,
                Name = customer.Name,
                NormalizedName = CustomerNames.Normalize(customer.Name),
                ContactPerson = customer.ContactPerson,
                Phone = customer.Phone,
                Email = customer.Email,
                IsActive = customer.IsActive,
                CreatedAt = now,
                UpdatedAt = now
            });

            foreach (var invoice in customer.Invoices)
            {
                var invoiceId = Guid.NewGuid();
                invoiceIds[invoice.RowId] = invoiceId;
                _db.Invoices.Add(new Invoice
                {
                    Id = invoiceId,
                    OrganizationId = orgId,
                    CustomerId = customer.CustomerId,
                    DataSourceId = sourceId,
                    ImportJobId = hasBank ? invoiceJobId : null,
                    RowId = invoice.RowId,
                    Number = invoice.Number,
                    InvoiceDate = invoice.InvoiceDate,
                    BookedDate = invoice.InvoiceDate,
                    DueDate = invoice.DueDate,
                    Amount = invoice.Amount,
                    AmountDue = invoice.AmountDue,
                    Status = invoice.Status,
                    PaidDate = invoice.PaidDate,
                    Currency = "ZAR",
                    Terms = invoice.Terms,
                    SourceRowNumber = sourceRow++,
                    ImportedAt = invoice.ImportedAt == default ? now : invoice.ImportedAt
                });

                var lineNumber = 1;
                foreach (var line in invoice.Lines)
                {
                    _db.InvoiceLines.Add(new InvoiceLine
                    {
                        Id = Guid.NewGuid(),
                        OrganizationId = orgId,
                        InvoiceId = invoiceId,
                        RowId = line.RowId,
                        LineNumber = lineNumber++,
                        Description = line.Description,
                        Amount = line.Amount
                    });
                }

                if (invoice.Status == InvoiceStatus.Paid && invoice.PaidDate is DateOnly paid)
                {
                    _db.Payments.Add(new Payment
                    {
                        Id = Guid.NewGuid(),
                        OrganizationId = orgId,
                        InvoiceId = invoiceId,
                        CustomerId = customer.CustomerId,
                        DataSourceId = sourceId,
                        ImportJobId = hasBank ? invoiceJobId : null,
                        RowId = "pay-" + invoice.RowId,
                        PaidDate = paid,
                        Amount = invoice.Amount,
                        Reference = invoice.Number,
                        SourceRowNumber = 1,
                        ImportedAt = now
                    });
                }
            }
        }

        var creditRow = 1;
        foreach (var note in creditNotes)
        {
            if (!invoiceIds.TryGetValue(note.InvoiceRowId, out var invoiceId))
            {
                throw new InvalidOperationException("A sample credit note does not match an invoice.");
            }

            var owner = customers.First(customer => customer.Invoices.Any(invoice => invoice.RowId == note.InvoiceRowId));
            _db.CreditNotes.Add(new CreditNote
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                InvoiceId = invoiceId,
                CustomerId = owner.CustomerId,
                DataSourceId = sourceId,
                ImportJobId = hasBank ? invoiceJobId : null,
                RowId = note.RowId,
                Number = note.Number,
                IssuedDate = note.IssuedDate,
                Amount = note.Amount,
                Reason = note.Reason,
                SourceRowNumber = creditRow++,
                ImportedAt = now
            });
        }

        foreach (var row in transactions)
        {
            _db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                ImportJobId = transactionJobId,
                DataSourceId = sourceId,
                RowId = row.RowId,
                SourceRowNumber = row.SourceRowNumber,
                Date = row.Date,
                BookedDate = row.BookedDate,
                Description = row.Description,
                Amount = row.Amount,
                Currency = "ZAR",
                Category = row.Category,
                Counterparty = row.Counterparty,
                Balance = row.Balance,
                ImportedAt = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Computed> ComputeAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.GetAsync(cancellationToken);
        var customers = await _db.Customers.AsNoTracking()
            .Include(customer => customer.Invoices)
            .ThenInclude(invoice => invoice.Lines)
            .ToListAsync(cancellationToken);

        var booksCustomers = customers.Select(MapCustomer).ToList();
        var countableDates = booksCustomers
            .SelectMany(customer => customer.Invoices)
            .Where(invoice => WhoToCallRules.IsCountableStatus(invoice.Status))
            .Select(invoice => invoice.InvoiceDate);
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var asAt = AsAtDates.Resolve(AsAtDates.Current, countableDates, today);
        var books = new WhoToCallBooks
        {
            AsAt = asAt,
            BusinessName = _currentUser.OrganizationName,
            Customers = booksCustomers
        };
        var thresholds = new WhoToCallThresholds(
            settings.StoppedMissedCycles,
            settings.DroppedPercent,
            settings.DroppedMonths,
            settings.MinimumInvoiceHistory);
        return new Computed(WhoToCallRules.Evaluate(books, thresholds), booksCustomers);
    }

    private static WhoToCallCustomerBook MapCustomer(Customer customer) =>
        new()
        {
            CustomerId = customer.Id,
            Name = customer.Name,
            RowId = customer.RowId,
            ContactPerson = customer.ContactPerson,
            Phone = customer.Phone,
            Email = customer.Email,
            IsActive = customer.IsActive,
            Invoices = customer.Invoices.Select(invoice => new WhoToCallInvoice
            {
                RowId = invoice.RowId,
                Number = invoice.Number,
                InvoiceDate = invoice.InvoiceDate,
                DueDate = invoice.DueDate,
                Amount = invoice.Amount,
                AmountDue = invoice.AmountDue,
                Status = invoice.Status,
                PaidDate = invoice.PaidDate,
                Terms = invoice.Terms,
                ImportedAt = invoice.ImportedAt,
                Lines = invoice.Lines
                    .OrderBy(line => line.LineNumber)
                    .Select(line => new WhoToCallLine
                    {
                        RowId = line.RowId,
                        Description = line.Description ?? string.Empty,
                        Amount = line.Amount
                    })
                    .ToList()
            }).ToList()
        };

    private async Task<IReadOnlyList<WhoToCallFlag>> VisibleFlagsAsync(Computed computed, CancellationToken cancellationToken)
    {
        var actions = await _db.CallCustomerActions.AsNoTracking()
            .OrderByDescending(item => item.At)
            .ToListAsync(cancellationToken);
        var latest = actions
            .GroupBy(item => item.CustomerId)
            .ToDictionary(group => group.Key, group => group.First());
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var books = computed.Customers.ToDictionary(item => item.CustomerId);
        var visible = new List<WhoToCallFlag>();
        foreach (var flag in computed.Result.Flags)
        {
            if (flag.CitedRowIds.Count == 0)
            {
                continue;
            }

            if (latest.TryGetValue(flag.CustomerId, out var action)
                && books.TryGetValue(flag.CustomerId, out var book)
                && IsHidden(action, flag, book, today))
            {
                continue;
            }

            flag.DraftBody = await DraftBodyAsync(flag, cancellationToken);
            visible.Add(flag);
        }

        return visible;
    }

    private static bool IsHidden(CallCustomerAction action, WhoToCallFlag flag, WhoToCallCustomerBook book, DateOnly today) =>
        action.Kind switch
        {
            CallActionKind.Called => false,
            CallActionKind.Snoozed => action.SnoozeUntil is DateOnly until && until >= today,
            CallActionKind.Paid => string.Equals(action.EvidenceKey, flag.EvidenceKey, StringComparison.Ordinal),
            CallActionKind.NotAConcern => !HasNewerBooks(book, action.At),
            _ => false
        };

    private static bool HasNewerBooks(WhoToCallCustomerBook book, DateTime actedAt)
    {
        var actedOn = DateOnly.FromDateTime(actedAt);
        return book.Invoices.Any(invoice =>
            WhoToCallRules.IsCountableStatus(invoice.Status)
            && (invoice.InvoiceDate > actedOn || invoice.ImportedAt > actedAt));
    }

    private async Task<string?> DraftBodyAsync(WhoToCallFlag flag, CancellationToken cancellationToken)
    {
        if (flag.Kind != CallFlagKind.Late)
        {
            return null;
        }

        var edited = await _db.ReminderDrafts.AsNoTracking()
            .FirstOrDefaultAsync(item => item.CustomerId == flag.CustomerId && item.EditedByOwner, cancellationToken);
        return edited is null ? flag.DraftBody : edited.Body;
    }

    private async Task<IReadOnlyList<CallActionDto>> HistoryForAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var rows = await _db.CallCustomerActions.AsNoTracking()
            .Where(item => item.CustomerId == customerId)
            .OrderByDescending(item => item.At)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var actorIds = rows.Select(item => item.ActedByUserId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(item => actorIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.DisplayName, cancellationToken);
        return rows.Select(item => new CallActionDto(
            item.Kind,
            item.At,
            item.SnoozeUntil,
            item.Note,
            names.TryGetValue(item.ActedByUserId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : "Someone")).ToList();
    }

    private async Task<(bool Using, string? Name)> SampleSourceAsync(CancellationToken cancellationToken)
    {
        var source = await _db.DataSources.AsNoTracking()
            .Where(item => item.ExternalSystem == "sample")
            .Select(item => new { item.Id, item.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (source is null)
        {
            return (false, null);
        }

        var invoices = await _db.Invoices.AsNoTracking().Select(item => item.DataSourceId).ToListAsync(cancellationToken);
        var usingSample = invoices.Count > 0 && invoices.All(id => id == source.Id);
        return (usingSample, usingSample ? source.Name : null);
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
            throw new InvalidOperationException("Only an owner can change who to call.");
        }
    }

    private sealed record Computed(WhoToCallResult Result, IReadOnlyList<WhoToCallCustomerBook> Customers);
}
