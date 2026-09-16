using System.Text;
using CruxAI.Core.Identity;
using CruxAI.Core.Mapping;
using CruxAI.Core.Parsing;
using CruxAI.Infrastructure.Csv;
using CruxAI.Infrastructure.Data;
using CruxAI.Infrastructure.Imports;
using CruxAI.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace CruxAI.Tests;

public class ImportPersistenceTests
{
    [Fact]
    public async Task Persist_writes_stable_row_ids_for_each_imported_line()
    {
        var root = Path.Combine(Path.GetTempPath(), "cruxai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var currentUser = new StubCurrentUser(orgId, userId);

        var options = new DbContextOptionsBuilder<CruxDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, "test.db")}")
            .Options;

        await using var db = new CruxDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Organizations.Add(new Core.Entities.Organization
        {
            Id = orgId,
            Name = "Harbour Street Studio",
            CreatedAt = DateTime.UtcNow
        });
        db.Users.Add(new Core.Entities.AppUser
        {
            Id = userId,
            Email = "owner@harbourstreet.local",
            DisplayName = "Demo Owner",
            ExternalId = "demo:owner@harbourstreet.local",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new ImportService(db, new LocalFileStorage(Path.Combine(root, "uploads")), new CsvHelperReader(), currentUser);

        const string csv = """
            Txn Date,Details,ZAR Amount,Category,Client,Ref
            2026-03-02,Cut and blow dry - Lerato,-450.00,Service,Lerato Moyo,INV-2104
            16/03/2026,Colour and treatment - Sipho,-1850.00,Service,Sipho Dlamini,INV-2105
            """;

        await using var upload = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var job = await service.CreateFromUploadAsync(upload, "sample-transactions.csv", upload.Length);

        var table = await service.LoadTableAsync(job);
        var mapping = service.GetMapping(job, table);
        Assert.Equal(TransactionFields.Date, mapping["Txn Date"]);
        await service.SaveMappingAsync(job, mapping);

        var validated = await service.ValidateAsync(job);
        Assert.All(validated, row => Assert.True(row.IsValid));

        var persisted = await service.PersistAsync(job, includeOnlyValid: false);

        Assert.Equal(2, persisted.Count);
        Assert.Equal(RowIdFactory.ForImportLine(job.Id, 2), persisted[0].RowId);
        Assert.Equal(RowIdFactory.ForImportLine(job.Id, 3), persisted[1].RowId);
        Assert.Equal(-450.00m, persisted[0].Amount);
        Assert.Equal("Cut and blow dry - Lerato", persisted[0].Description);

        var recent = await service.ListRecentAsync();
        Assert.Contains(recent, item => item.Id == job.Id);
    }

    [Fact]
    public async Task Csv_reader_accepts_semicolon_delimited_exports()
    {
        const string csv = "Date;Description;Amount\n2026-03-01;Cut;-100.00\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var table = await new CsvHelperReader().ReadAsync(stream);
        Assert.Equal(new[] { "Date", "Description", "Amount" }, table.Headers);
        Assert.Single(table.Rows);
        Assert.Equal("Cut", table.Rows[0].Values["Description"]);
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public StubCurrentUser(Guid organizationId, Guid userId)
        {
            OrganizationId = organizationId;
            UserId = userId;
        }

        public bool IsAuthenticated => true;
        public Guid UserId { get; }
        public Guid OrganizationId { get; }
        public string Email => "owner@harbourstreet.local";
        public string DisplayName => "Demo Owner";
        public string OrganizationName => "Harbour Street Studio";
    }
}
