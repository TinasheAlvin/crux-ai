using VhonaAI.Application.Imports;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Parsing;
using VhonaAI.Core.Validation;
using VhonaAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Infrastructure.Imports;

/// <summary>
/// Turns a validated invoice or debtors file into customers, invoices, lines, payments and credit notes.
/// Amount due is stored as the file states it. Credit notes are linked and do not recompute that balance.
/// </summary>
internal sealed class InvoiceImportPersister
{
    private readonly VhonaDbContext _db;

    public InvoiceImportPersister(VhonaDbContext db)
    {
        _db = db;
    }

    public async Task<InvoiceImportResult> PersistAsync(
        ImportJob job,
        IReadOnlyList<ValidatedInvoiceRow> rows,
        bool includeOnlyValid,
        CancellationToken cancellationToken)
    {
        var valid = rows.Where(row => row.IsValid).Select(row => row.Parsed!).ToList();
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

        var excel = job.OriginalFileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
        var source = await ImportSourceFactory.GetOrCreateAsync(
            _db,
            job,
            excel ? DataSourceKind.InvoiceExcel : DataSourceKind.InvoiceCsv,
            excel ? "xlsx" : "csv",
            cancellationToken);

        var now = DateTime.UtcNow;
        var customers = new HashSet<Guid>();
        var invoices = new List<Invoice>();
        var lineCount = 0;
        var paymentCount = 0;
        var creditCount = 0;

        foreach (var group in valid
                     .Where(row => row.Kind == ImportedLedgerRowKind.Invoice)
                     .GroupBy(row => row.InvoiceNumber!, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(row => row.SourceRowNumber).ToList();
            var header = ordered[0];
            var customer = await UpsertCustomerAsync(job.OrganizationId, source.Id, header, now, cancellationToken);
            customers.Add(customer.Id);
            var invoice = await UpsertInvoiceAsync(job, source, customer, header, now, cancellationToken);
            invoices.Add(invoice);

            var lineNumber = 1;
            foreach (var row in ordered)
            {
                if (!row.HasLineDetail && ordered.Count == 1)
                {
                    continue;
                }

                await UpsertLineAsync(job, invoice, row, lineNumber++, cancellationToken);
                lineCount++;
            }

            var paid = PaidPortion(header);
            if (header.PaidDate is not null && paid > 0)
            {
                await UpsertPaymentAsync(
                    job,
                    source,
                    customer,
                    invoice,
                    RowIdFactory.ForPayment(job.Id, header.SourceRowNumber),
                    header.PaidDate.Value,
                    paid,
                    header.InvoiceNumber,
                    header.SourceRowNumber,
                    now,
                    cancellationToken);
                paymentCount++;
            }
        }

        foreach (var row in valid.Where(row => row.Kind == ImportedLedgerRowKind.Payment))
        {
            var customer = await FindOrUpsertCustomerAsync(job.OrganizationId, source.Id, row, now, cancellationToken);
            if (customer is not null)
            {
                customers.Add(customer.Id);
            }

            var invoice = await FindInvoiceAsync(job, row.InvoiceNumber, cancellationToken);
            var amount = row.PaymentAmount ?? row.Amount ?? 0;
            var paidDate = row.PaidDate ?? row.InvoiceDate
                ?? throw new InvalidOperationException("Paid date is required.");
            await UpsertPaymentAsync(
                job,
                source,
                customer,
                invoice,
                RowIdFactory.ForPayment(job.Id, row.SourceRowNumber),
                paidDate,
                amount,
                row.InvoiceNumber,
                row.SourceRowNumber,
                now,
                cancellationToken);
            paymentCount++;
        }

        foreach (var row in valid.Where(row => row.Kind == ImportedLedgerRowKind.CreditNote))
        {
            var customer = await FindOrUpsertCustomerAsync(job.OrganizationId, source.Id, row, now, cancellationToken);
            if (customer is not null)
            {
                customers.Add(customer.Id);
            }

            var invoice = await FindInvoiceAsync(job, row.InvoiceNumber, cancellationToken);
            var amount = Math.Abs(row.CreditAmount ?? row.Amount ?? 0);
            var issued = row.InvoiceDate ?? row.PaidDate
                ?? throw new InvalidOperationException("Credit note date is required.");
            await UpsertCreditNoteAsync(
                job,
                source,
                customer,
                invoice,
                row,
                amount,
                issued,
                now,
                cancellationToken);
            creditCount++;
        }

        job.Status = ImportStatus.Imported;
        job.ImportedRowCount = valid.Count;
        job.UpdatedAt = now;
        job.ErrorSummary = invalidCount > 0 ? $"{invalidCount} invalid row(s) skipped." : null;
        await _db.SaveChangesAsync(cancellationToken);

        return new InvoiceImportResult
        {
            CustomerCount = customers.Count,
            InvoiceCount = invoices.Count,
            LineCount = lineCount,
            PaymentCount = paymentCount,
            CreditNoteCount = creditCount,
            Invoices = invoices.OrderBy(invoice => invoice.SourceRowNumber).ToList()
        };
    }

    private async Task<Customer> UpsertCustomerAsync(
        Guid organizationId,
        Guid dataSourceId,
        ParsedInvoiceRow row,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(row.CustomerName))
        {
            throw new InvalidOperationException("Customer name is required.");
        }

        var normalized = CustomerNames.Normalize(row.CustomerName);
        Customer? customer = null;
        if (!string.IsNullOrWhiteSpace(row.CustomerSourceId))
        {
            customer = _db.Customers.Local.FirstOrDefault(
                item => item.OrganizationId == organizationId && item.SourceExternalId == row.CustomerSourceId);
            customer ??= await _db.Customers.FirstOrDefaultAsync(
                item => item.OrganizationId == organizationId && item.SourceExternalId == row.CustomerSourceId,
                cancellationToken);
        }

        customer ??= _db.Customers.Local.FirstOrDefault(
            item => item.OrganizationId == organizationId && item.NormalizedName == normalized);
        customer ??= await _db.Customers.FirstOrDefaultAsync(
            item => item.OrganizationId == organizationId && item.NormalizedName == normalized,
            cancellationToken);

        if (customer is null)
        {
            customer = new Customer
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                DataSourceId = dataSourceId,
                RowId = RowIdFactory.ForCustomer(organizationId, normalized),
                Name = row.CustomerName.Trim(),
                NormalizedName = normalized,
                IsActive = true,
                CreatedAt = now
            };
            _db.Customers.Add(customer);
        }
        else if (!string.IsNullOrWhiteSpace(row.CustomerName))
        {
            customer.Name = row.CustomerName.Trim();
        }

        customer.ContactPerson = Prefer(row.ContactPerson, customer.ContactPerson);
        customer.Phone = Prefer(row.Phone, customer.Phone);
        customer.Email = Prefer(row.Email, customer.Email);
        if (string.IsNullOrWhiteSpace(customer.SourceExternalId))
        {
            customer.SourceExternalId = row.CustomerSourceId;
        }

        customer.UpdatedAt = now;
        return customer;
    }

    private async Task<Customer?> FindOrUpsertCustomerAsync(
        Guid organizationId,
        Guid dataSourceId,
        ParsedInvoiceRow row,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(row.CustomerName))
        {
            return null;
        }

        return await UpsertCustomerAsync(organizationId, dataSourceId, row, now, cancellationToken);
    }

    private async Task<Invoice> UpsertInvoiceAsync(
        ImportJob job,
        DataSource source,
        Customer customer,
        ParsedInvoiceRow header,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var rowId = RowIdFactory.ForInvoice(job.Id, header.SourceRowNumber);
        var invoice = await _db.Invoices.FirstOrDefaultAsync(
            item => item.OrganizationId == job.OrganizationId && item.RowId == rowId,
            cancellationToken);
        if (invoice is null)
        {
            invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                OrganizationId = job.OrganizationId,
                RowId = rowId
            };
            _db.Invoices.Add(invoice);
        }

        invoice.CustomerId = customer.Id;
        invoice.DataSourceId = source.Id;
        invoice.ImportJobId = job.Id;
        invoice.Number = header.InvoiceNumber!.Trim();
        invoice.InvoiceDate = header.InvoiceDate!.Value;
        invoice.BookedDate = header.BookedDate;
        invoice.DueDate = header.DueDate;
        invoice.Amount = header.Amount!.Value;
        invoice.AmountDue = header.AmountDue ?? header.Amount.Value;
        invoice.Status = header.Status ?? InvoiceStatus.Open;
        invoice.PaidDate = header.PaidDate;
        invoice.Currency = header.Currency;
        invoice.Terms = header.Terms;
        invoice.SourceRowNumber = header.SourceRowNumber;
        invoice.ImportedAt = now;
        return invoice;
    }

    private async Task UpsertLineAsync(
        ImportJob job,
        Invoice invoice,
        ParsedInvoiceRow row,
        int lineNumber,
        CancellationToken cancellationToken)
    {
        var rowId = RowIdFactory.ForInvoiceLine(job.Id, row.SourceRowNumber);
        var line = await _db.InvoiceLines.FirstOrDefaultAsync(
            item => item.OrganizationId == job.OrganizationId && item.RowId == rowId,
            cancellationToken);
        if (line is null)
        {
            line = new InvoiceLine
            {
                Id = Guid.NewGuid(),
                OrganizationId = job.OrganizationId,
                RowId = rowId
            };
            _db.InvoiceLines.Add(line);
        }

        line.InvoiceId = invoice.Id;
        line.LineNumber = lineNumber;
        line.Description = row.LineDescription;
        line.ItemCode = row.ItemCode;
        line.Amount = row.LineAmount ?? row.Amount ?? 0;
        line.Quantity = row.Quantity;
    }

    private async Task UpsertPaymentAsync(
        ImportJob job,
        DataSource source,
        Customer? customer,
        Invoice? invoice,
        string rowId,
        DateOnly paidDate,
        decimal amount,
        string? reference,
        int sourceRowNumber,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var payment = await _db.Payments.FirstOrDefaultAsync(
            item => item.OrganizationId == job.OrganizationId && item.RowId == rowId,
            cancellationToken);
        if (payment is null)
        {
            payment = new Payment
            {
                Id = Guid.NewGuid(),
                OrganizationId = job.OrganizationId,
                RowId = rowId
            };
            _db.Payments.Add(payment);
        }

        payment.InvoiceId = invoice?.Id;
        payment.CustomerId = customer?.Id ?? invoice?.CustomerId;
        payment.DataSourceId = source.Id;
        payment.ImportJobId = job.Id;
        payment.PaidDate = paidDate;
        payment.Amount = amount;
        payment.Reference = reference;
        payment.SourceRowNumber = sourceRowNumber;
        payment.ImportedAt = now;
    }

    private async Task UpsertCreditNoteAsync(
        ImportJob job,
        DataSource source,
        Customer? customer,
        Invoice? invoice,
        ParsedInvoiceRow row,
        decimal amount,
        DateOnly issued,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var rowId = RowIdFactory.ForCreditNote(job.Id, row.SourceRowNumber);
        var note = await _db.CreditNotes.FirstOrDefaultAsync(
            item => item.OrganizationId == job.OrganizationId && item.RowId == rowId,
            cancellationToken);
        if (note is null)
        {
            note = new CreditNote
            {
                Id = Guid.NewGuid(),
                OrganizationId = job.OrganizationId,
                RowId = rowId
            };
            _db.CreditNotes.Add(note);
        }

        note.InvoiceId = invoice?.Id;
        note.CustomerId = customer?.Id ?? invoice?.CustomerId;
        note.DataSourceId = source.Id;
        note.ImportJobId = job.Id;
        note.Number = row.CreditNoteNumber ?? row.InvoiceNumber;
        note.IssuedDate = issued;
        note.Amount = amount;
        note.Reason = row.LineDescription;
        note.SourceRowNumber = row.SourceRowNumber;
        note.ImportedAt = now;
    }

    private async Task<Invoice?> FindInvoiceAsync(ImportJob job, string? number, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return null;
        }

        var trimmed = number.Trim();
        var tracked = _db.Invoices.Local
            .Where(invoice => invoice.OrganizationId == job.OrganizationId
                              && string.Equals(invoice.Number, trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(invoice => invoice.ImportJobId == job.Id)
            .ThenByDescending(invoice => invoice.InvoiceDate)
            .FirstOrDefault();
        if (tracked is not null)
        {
            return tracked;
        }

        return await _db.Invoices
            .Where(invoice => invoice.OrganizationId == job.OrganizationId && invoice.Number == trimmed)
            .OrderByDescending(invoice => invoice.ImportJobId == job.Id)
            .ThenByDescending(invoice => invoice.InvoiceDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static decimal PaidPortion(ParsedInvoiceRow header)
    {
        if (header.Amount is null)
        {
            return 0;
        }

        if (header.AmountDue is null)
        {
            return header.Status == InvoiceStatus.Paid ? header.Amount.Value : 0;
        }

        var paid = header.Amount.Value - header.AmountDue.Value;
        return paid > 0 ? paid : 0;
    }

    private static string? Prefer(string? incoming, string? current) =>
        string.IsNullOrWhiteSpace(incoming) ? current : incoming.Trim();
}
