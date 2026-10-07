using System.Text;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Identity;
using VhonaAI.Core.Mapping;
using VhonaAI.Core.Parsing;
using VhonaAI.Infrastructure.Csv;
using VhonaAI.Infrastructure.Data;
using VhonaAI.Infrastructure.Imports;
using VhonaAI.Infrastructure.Storage;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Tests;

public class InvoiceImportTests
{
    [Fact]
    public async Task Csv_import_creates_customers_invoices_lines_payments_and_credit_notes()
    {
        var (service, db, orgId) = await CreateAsync();
        await using (db)
        {
            const string csv = """
                Customer,Contact,Phone,Email,Invoice Number,Invoice Date,Booked Date,Due Date,Amount,Amount Due,Status,Paid Date,Line Description,Row Type,Credit Amount,Credit Note Number
                Lerato Moyo,Lerato,0820001111,lerato@example.com,INV-1,2026-03-02,2026-03-03,2026-03-16,450.00,0,Paid,2026-03-10,Cut and blow,Invoice,,
                Sipho Dlamini,Sipho,0820002222,sipho@example.com,INV-2,2026-03-16,,2026-04-15,1850.00,1850.00,Open,,Colour,Invoice,,
                Lerato Moyo,,,,INV-1,2026-03-12,,,200.00,,,,,Payment,,
                Lerato Moyo,,,,INV-1,2026-03-20,,,,,,,,Credit,100,CN-9
                """;

            var result = await ImportInvoicesAsync(service, csv, "debtors.csv");
            Assert.Equal(2, result.CustomerCount);
            Assert.Equal(2, result.InvoiceCount);
            Assert.Equal(2, result.LineCount);
            Assert.Equal(2, result.PaymentCount);
            Assert.Equal(1, result.CreditNoteCount);

            var lerato = await db.Customers.SingleAsync(item => item.Name == "Lerato Moyo");
            Assert.Equal(RowIdFactory.ForCustomer(orgId, CustomerNames.Normalize("Lerato Moyo")), lerato.RowId);
            Assert.Equal("Lerato", lerato.ContactPerson);
            Assert.Equal("0820001111", lerato.Phone);
            Assert.Equal("lerato@example.com", lerato.Email);

            var paid = await db.Invoices.Include(item => item.Customer).SingleAsync(item => item.Number == "INV-1");
            Assert.Equal(new DateOnly(2026, 3, 2), paid.InvoiceDate);
            Assert.Equal(new DateOnly(2026, 3, 3), paid.BookedDate);
            Assert.Equal(new DateOnly(2026, 3, 16), paid.DueDate);
            Assert.Equal(450.00m, paid.Amount);
            Assert.Equal(0m, paid.AmountDue);
            Assert.Equal(InvoiceStatus.Paid, paid.Status);
            Assert.Equal(new DateOnly(2026, 3, 10), paid.PaidDate);
            Assert.StartsWith("inv_", paid.RowId);

            var credit = await db.CreditNotes.SingleAsync();
            Assert.Equal(100m, credit.Amount);
            Assert.Equal("CN-9", credit.Number);
            Assert.Equal(paid.Id, credit.InvoiceId);
            Assert.Equal(0m, paid.AmountDue);

            var source = await db.DataSources.SingleAsync();
            Assert.Equal(DataSourceKind.InvoiceCsv, source.Kind);
            Assert.Equal("csv", source.ExternalSystem);
            Assert.Equal(orgId, source.OrganizationId);
        }
    }

    [Fact]
    public async Task A_later_import_reuses_the_customer_row_id_and_mints_a_new_invoice_row_id()
    {
        var (service, db, orgId) = await CreateAsync();
        await using (db)
        {
            const string first = """
                Customer,Invoice Number,Invoice Date,Amount
                Lerato Moyo,INV-1,2026-03-02,450.00
                """;
            const string second = """
                Customer,Invoice Number,Invoice Date,Amount
                lerato   moyo,INV-9,2026-04-02,900.00
                """;

            var firstResult = await ImportInvoicesAsync(service, first, "one.csv");
            var secondResult = await ImportInvoicesAsync(service, second, "two.csv");

            var customer = await db.Customers.SingleAsync();
            Assert.Equal(RowIdFactory.ForCustomer(orgId, "LERATO MOYO"), customer.RowId);
            Assert.Equal("LERATO MOYO", customer.NormalizedName);
            Assert.Equal("lerato   moyo", customer.Name);
            Assert.NotEqual(firstResult.Invoices[0].RowId, secondResult.Invoices[0].RowId);
            Assert.Equal(RowIdFactory.ForInvoice(secondResult.Invoices[0].ImportJobId!.Value, secondResult.Invoices[0].SourceRowNumber), secondResult.Invoices[0].RowId);
        }
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task Correctly_mapped_invoice_file_confirms_and_imports(string fileKind)
    {
        var (service, db, _) = await CreateAsync();
        await using (db)
        {
            var job = fileKind == "xlsx"
                ? await UploadWorkbookAsync(service)
                : await UploadCsvJobAsync(service, """
                    Customer,Invoice Number,Invoice Date,Amount,Status,Amount Due
                    Naledi Khumalo,INV-2295,2026-01-13,12400.00,Overdue,12400.00
                    """, "invoices.csv");

            var mapping = service.GetMapping(job, await service.LoadTableAsync(job));
            Assert.Equal(InvoiceFields.CustomerName, mapping["Customer"]);
            Assert.Equal(InvoiceFields.InvoiceDate, mapping["Invoice Date"]);
            Assert.DoesNotContain(TransactionFields.Date, mapping.Values);
            Assert.DoesNotContain(TransactionFields.Description, mapping.Values);

            var confirmErrors = ImportMapping.Validate(job.Kind, mapping);
            Assert.Empty(confirmErrors);

            await service.SaveMappingAsync(job, mapping);
            var result = await service.PersistInvoicesAsync(job, includeOnlyValid: false);

            Assert.Equal(1, result.InvoiceCount);
            var invoice = await db.Invoices.SingleAsync();
            Assert.Equal("INV-2295", invoice.Number);
            Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
            Assert.Equal(12400.00m, invoice.AmountDue);
        }
    }

    [Fact]
    public void Overdue_status_is_kept_instead_of_collapsed_to_open()
    {
        Assert.True(InvoiceStatusParser.TryParse("Overdue", out var overdue));
        Assert.Equal(InvoiceStatus.Overdue, overdue);
        Assert.NotEqual(InvoiceStatus.Open, overdue);
        Assert.True(InvoiceStatusParser.TryParse("Open", out var open));
        Assert.Equal(InvoiceStatus.Open, open);
    }

    [Fact]
    public async Task Excel_import_uses_the_same_mapping_flow()
    {
        var (service, db, _) = await CreateAsync();
        await using (db)
        {
            await using var workbookStream = new MemoryStream();
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Debtors");
                sheet.Cell(1, 1).Value = "Customer";
                sheet.Cell(1, 2).Value = "Invoice Number";
                sheet.Cell(1, 3).Value = "Invoice Date";
                sheet.Cell(1, 4).Value = "Amount";
                sheet.Cell(2, 1).Value = "Sipho Dlamini";
                sheet.Cell(2, 2).Value = "INV-2";
                sheet.Cell(2, 3).Value = "2026-03-16";
                sheet.Cell(2, 4).Value = "1850.00";
                workbook.SaveAs(workbookStream);
            }

            workbookStream.Position = 0;
            var job = await service.CreateFromUploadAsync(workbookStream, "debtors.xlsx", workbookStream.Length, ImportKind.Invoices);
            var table = await service.LoadTableAsync(job);
            var mapping = service.GetMapping(job, table);
            Assert.Equal(InvoiceFields.CustomerName, mapping["Customer"]);
            Assert.Equal(InvoiceFields.Amount, mapping["Amount"]);
            await service.SaveMappingAsync(job, mapping);
            var result = await service.PersistInvoicesAsync(job, includeOnlyValid: false);

            Assert.Equal(1, result.InvoiceCount);
            Assert.Equal(DataSourceKind.InvoiceExcel, (await db.DataSources.SingleAsync()).Kind);
            Assert.Equal("Sipho Dlamini", (await db.Customers.SingleAsync()).Name);
        }
    }

    [Fact]
    public async Task Repeated_invoice_numbers_without_lines_are_rejected()
    {
        var (service, db, _) = await CreateAsync();
        await using (db)
        {
            const string csv = """
                Customer,Invoice Number,Invoice Date,Amount
                Lerato Moyo,INV-1,2026-03-02,450.00
                Lerato Moyo,INV-1,2026-03-02,450.00
                """;
            var job = await UploadAsync(service, csv, "dup.csv");
            var rows = await service.ValidateInvoicesAsync(job);
            Assert.True(rows[0].IsValid);
            Assert.False(rows[1].IsValid);
            Assert.Contains("repeated", rows[1].Errors[InvoiceFields.InvoiceNumber], StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_payments_only_file_is_not_a_valid_invoice_mapping()
    {
        var (service, db, _) = await CreateAsync();
        await using (db)
        {
            const string csv = """
                Customer,Paid Date,Payment Amount
                Lerato Moyo,2026-03-01,100.00
                """;
            var job = await service.CreateFromUploadAsync(
                new MemoryStream(Encoding.UTF8.GetBytes(csv)),
                "payments.csv",
                csv.Length,
                ImportKind.Invoices);
            var mapping = service.GetMapping(job, await service.LoadTableAsync(job));
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveMappingAsync(job, mapping));
            Assert.Contains("InvoiceNumber", ex.Message);
        }
    }

    [Fact]
    public async Task Transaction_import_keeps_the_document_date_and_stores_booked_date()
    {
        var (service, db, orgId) = await CreateAsync();
        await using (db)
        {
            const string csv = """
                Txn Date,Booked Date,Details,ZAR Amount
                2026-03-02,2026-03-01,Cut and blow,-450.00
                """;
            await using var upload = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var job = await service.CreateFromUploadAsync(upload, "bank.csv", upload.Length);
            await service.SaveMappingAsync(job, service.GetMapping(job, await service.LoadTableAsync(job)));
            var persisted = await service.PersistAsync(job, includeOnlyValid: false);

            Assert.Equal(new DateOnly(2026, 3, 2), persisted[0].Date);
            Assert.Equal(new DateOnly(2026, 3, 1), persisted[0].BookedDate);
            Assert.Equal(DataSourceKind.TransactionCsv, (await db.DataSources.SingleAsync()).Kind);
            Assert.Equal(orgId, persisted[0].OrganizationId);
        }
    }

    [Fact]
    public async Task Another_business_cannot_read_imported_invoices()
    {
        var root = Path.Combine(Path.GetTempPath(), "vhonaai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var options = Options(root);
        var studio = Guid.NewGuid();
        var bakery = Guid.NewGuid();
        var studioUser = new FixedUser(studio);
        var bakeryUser = new FixedUser(bakery);
        await using (var db = new VhonaDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var now = DateTime.UtcNow;
            db.Organizations.Add(new Organization { Id = studio, Name = "Studio", CreatedAt = now });
            db.Organizations.Add(new Organization { Id = bakery, Name = "Bakery", CreatedAt = now });
            db.Users.Add(new AppUser
            {
                Id = studioUser.UserId,
                Email = "studio@example.com",
                DisplayName = "Studio",
                ExternalId = "oid-studio",
                CreatedAt = now
            });
            db.Users.Add(new AppUser
            {
                Id = bakeryUser.UserId,
                Email = "bakery@example.com",
                DisplayName = "Bakery",
                ExternalId = "oid-bakery",
                CreatedAt = now
            });
            await db.SaveChangesAsync();
        }

        await using var studioDb = new VhonaDbContext(options, studioUser);
        var studioImports = new ImportService(studioDb, new LocalFileStorage(Path.Combine(root, "uploads")), new CsvHelperReader(), studioUser);
        await ImportInvoicesAsync(studioImports, """
            Customer,Invoice Number,Invoice Date,Amount
            Lerato Moyo,INV-1,2026-03-02,450.00
            """, "studio.csv");

        await using var bakeryDb = new VhonaDbContext(options, bakeryUser);
        Assert.Empty(await bakeryDb.Invoices.ToListAsync());
        Assert.Empty(await bakeryDb.Customers.ToListAsync());
        Assert.Equal(1, await bakeryDb.Invoices.IgnoreQueryFilters().CountAsync());
    }

    private static async Task<Application.Imports.InvoiceImportResult> ImportInvoicesAsync(ImportService service, string csv, string fileName)
    {
        var job = await UploadAsync(service, csv, fileName);
        return await service.PersistInvoicesAsync(job, includeOnlyValid: false);
    }

    private static async Task<ImportJob> UploadAsync(ImportService service, string csv, string fileName)
    {
        var job = await UploadCsvJobAsync(service, csv, fileName);
        var mapping = service.GetMapping(job, await service.LoadTableAsync(job));
        await service.SaveMappingAsync(job, mapping);
        return job;
    }

    private static async Task<ImportJob> UploadCsvJobAsync(ImportService service, string csv, string fileName)
    {
        await using var upload = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return await service.CreateFromUploadAsync(upload, fileName, upload.Length, ImportKind.Invoices);
    }

    private static async Task<ImportJob> UploadWorkbookAsync(ImportService service)
    {
        await using var workbookStream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Debtors");
            sheet.Cell(1, 1).Value = "Customer";
            sheet.Cell(1, 2).Value = "Invoice Number";
            sheet.Cell(1, 3).Value = "Invoice Date";
            sheet.Cell(1, 4).Value = "Amount";
            sheet.Cell(1, 5).Value = "Status";
            sheet.Cell(1, 6).Value = "Amount Due";
            sheet.Cell(2, 1).Value = "Naledi Khumalo";
            sheet.Cell(2, 2).Value = "INV-2295";
            sheet.Cell(2, 3).Value = "2026-01-13";
            sheet.Cell(2, 4).Value = "12400.00";
            sheet.Cell(2, 5).Value = "Overdue";
            sheet.Cell(2, 6).Value = "12400.00";
            workbook.SaveAs(workbookStream);
        }

        workbookStream.Position = 0;
        return await service.CreateFromUploadAsync(workbookStream, "invoices.xlsx", workbookStream.Length, ImportKind.Invoices);
    }

    private static async Task<(ImportService Service, VhonaDbContext Db, Guid OrgId)> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "vhonaai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var user = new FixedUser(orgId, userId);
        var db = new VhonaDbContext(Options(root), user);
        await db.Database.EnsureCreatedAsync();
        db.Organizations.Add(new Organization { Id = orgId, Name = "Ada Studio", CreatedAt = DateTime.UtcNow });
        db.Users.Add(new AppUser
        {
            Id = userId,
            Email = "ada@example.com",
            DisplayName = "Ada",
            ExternalId = "oid-ada",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new ImportService(db, new LocalFileStorage(Path.Combine(root, "uploads")), new CsvHelperReader(), user);
        return (service, db, orgId);
    }

    private static DbContextOptions<VhonaDbContext> Options(string root) =>
        new DbContextOptionsBuilder<VhonaDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, "test.db")}")
            .Options;

    private sealed class FixedUser : ICurrentUser
    {
        public FixedUser(Guid organizationId, Guid? userId = null)
        {
            OrganizationId = organizationId;
            UserId = userId ?? Guid.NewGuid();
        }

        public bool IsAuthenticated => true;
        public Guid UserId { get; }
        public Guid OrganizationId { get; }
        public string Email => "ada@example.com";
        public string DisplayName => "Ada";
        public string OrganizationName => "Ada Studio";
        public string Role => "Owner";
    }
}
