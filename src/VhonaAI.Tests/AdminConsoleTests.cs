using System.Reflection;
using System.Text.Json;
using VhonaAI.Application.Calling;
using VhonaAI.Core.Admin;
using VhonaAI.Core.Analytics;
using VhonaAI.Core.Calling;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Identity;
using VhonaAI.Core.Storage;
using VhonaAI.Infrastructure.Admin;
using VhonaAI.Infrastructure.Calling;
using VhonaAI.Infrastructure.Data;
using VhonaAI.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace VhonaAI.Tests;

public class AdminConsoleTests
{
    [Fact]
    public void Admin_list_matches_email_object_id_or_the_legacy_partner_list()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:VhonaAdminEmails"] = "founder@vhona.test, other@vhona.test",
            ["Auth:VhonaAdminObjectIds"] = "oid-founder",
            ["Auth:InternalAdminEmails"] = "legacy@vhona.test"
        }).Build();

        Assert.True(VhonaAdmins.IsMatch(config, "Founder@Vhona.Test", null));
        Assert.True(VhonaAdmins.IsMatch(config, null, "OID-FOUNDER"));
        Assert.True(VhonaAdmins.IsMatch(config, "legacy@vhona.test", null));
        Assert.False(VhonaAdmins.IsMatch(config, "owner@harbourstreet.local", "someone-else"));
        Assert.False(VhonaAdmins.IsMatch(new ConfigurationBuilder().Build(), "founder@vhona.test", "oid-founder"));
    }

    [Theory]
    [InlineData(false, false, AdminRouteDecision.Challenge)]
    [InlineData(false, true, AdminRouteDecision.Challenge)]
    [InlineData(true, false, AdminRouteDecision.NotFound)]
    [InlineData(true, true, AdminRouteDecision.Allow)]
    public void Admin_routes_hide_everyone_except_a_vhona_admin(bool signedIn, bool admin, AdminRouteDecision expected)
    {
        Assert.Equal(expected, AdminRouteGuard.Decide(signedIn, admin));
    }

    [Fact]
    public void Disabled_accounts_and_businesses_stay_out_of_the_product()
    {
        Assert.Equal(ProductAccess.AccountDisabled, ProductAccessGate.Decide(true, userDisabled: true, organizationDisabled: false, "/admin"));
        Assert.Equal(ProductAccess.BusinessDisabled, ProductAccessGate.Decide(false, userDisabled: false, organizationDisabled: true, "/"));
        Assert.Equal(ProductAccess.BusinessDisabled, ProductAccessGate.Decide(true, userDisabled: false, organizationDisabled: true, "/health"));
        Assert.Equal(ProductAccess.Continue, ProductAccessGate.Decide(true, userDisabled: false, organizationDisabled: true, "/admin/businesses"));
        Assert.Equal(ProductAccess.Continue, ProductAccessGate.Decide(true, userDisabled: true, organizationDisabled: true, "/account-disabled"));
        Assert.Equal(ProductAccess.Continue, ProductAccessGate.Decide(false, userDisabled: false, organizationDisabled: false, "/"));
        Assert.True(ProductAccessGate.SkipsAccountCheck("/_framework/blazor.server.js"));
    }

    [Fact]
    public async Task A_non_admin_cannot_call_any_admin_operation()
    {
        var (db, service, _) = await OpenAsync(isAdmin: false);
        await using (db)
        {
            foreach (var method in typeof(AdminConsoleService).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                var args = method.GetParameters().Select(ArgumentFor).ToArray();
                var pending = method.Invoke(service, args);
                var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => (Task)pending!);
                Assert.Equal("Vhona admin only.", ex.Message);
            }

            Assert.Equal(0, await db.AdminAuditEntries.CountAsync());
            Assert.Equal(0, await db.Organizations.IgnoreQueryFilters().CountAsync());
        }
    }

    [Fact]
    public void An_admin_page_view_is_recorded_on_the_interactive_pass_only()
    {
        var guard = new AdminViewGuard();
        Assert.False(guard.TryRecord("console"));

        guard.MarkInteractive();
        Assert.True(guard.TryRecord("console"));
        Assert.False(guard.TryRecord("console"));
        Assert.True(guard.TryRecord("business-2"));
    }

    [Fact]
    public async Task Opening_the_admin_console_writes_one_audit_row()
    {
        var (db, service, _) = await OpenWorldAsync();
        await using (db)
        {
            var guard = new AdminViewGuard();
            if (guard.TryRecord("console"))
            {
                await service.GetOverviewAsync();
            }

            guard.MarkInteractive();
            if (guard.TryRecord("console"))
            {
                await service.GetOverviewAsync();
            }

            if (guard.TryRecord("console"))
            {
                await service.GetOverviewAsync();
            }

            Assert.Equal(1, await db.AdminAuditEntries.CountAsync(item => item.Action == "console.open"));
        }
    }

    [Fact]
    public async Task Admin_views_and_changes_are_audited_and_the_tenant_filter_stays_on()
    {
        var (db, service, world) = await OpenWorldAsync();
        await using (db)
        {
            Assert.Equal(new[] { "Lerato Moyo" }, await db.Customers.Select(item => item.Name).ToListAsync());

            var listed = await service.SearchBusinessesAsync("studio");
            Assert.Single(listed);
            Assert.Equal("Studio Books", listed[0].Name);
            Assert.Equal("founder@vhona.local", listed[0].OwnerEmail);

            var detail = await service.GetBusinessAsync(world.BakeryId);
            Assert.Equal(1, detail.CustomerCount);
            Assert.Equal("Bakery Only", detail.Name);

            var blockedRemoval = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RemoveMemberAsync(world.OwnerMembershipId));
            Assert.Contains("needs an owner", blockedRemoval.Message, StringComparison.OrdinalIgnoreCase);
            var blockedDemotion = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SetUserRoleAsync(world.OwnerMembershipId, "Member"));
            Assert.Contains("needs an owner", blockedDemotion.Message, StringComparison.OrdinalIgnoreCase);

            await service.RenameBusinessAsync(world.StudioId, "  Studio Books East  ");
            await service.SetBusinessDisabledAsync(world.StudioId, true);
            await service.SetUserRoleAsync(world.MemberMembershipId, "Owner");

            var promote = await db.AdminAuditEntries.SingleAsync(item => item.Action == "user.role");
            Assert.Equal(world.Admin.UserId, promote.ActorUserId);
            Assert.Equal(world.StudioId, promote.OrganizationId);
            Assert.Contains("Member to Owner", promote.Summary);

            var renamed = await db.Organizations.IgnoreQueryFilters().SingleAsync(item => item.Id == world.StudioId);
            Assert.Equal("Studio Books East", renamed.Name);
            Assert.NotNull(renamed.DisabledAt);

            Assert.Equal(2, await db.Memberships.IgnoreQueryFilters().CountAsync(item => item.OrganizationId == world.StudioId));

            await service.RemoveMemberAsync(world.MemberMembershipId);
            Assert.DoesNotContain(
                await db.Memberships.IgnoreQueryFilters().Select(item => item.Id).ToListAsync(),
                id => id == world.MemberMembershipId);

            var self = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SetUserDisabledAsync(world.Admin.UserId, true));
            Assert.Contains("your own sign-in", self.Message, StringComparison.OrdinalIgnoreCase);
            await service.SetUserDisabledAsync(world.MemberId, true);
            Assert.NotNull((await db.Users.SingleAsync(item => item.Id == world.MemberId)).DisabledAt);

            var issued = await service.ResendInviteAsync(world.InviteId);
            Assert.NotEqual(world.InviteToken, issued.Token);
            var resent = await db.AdminAuditEntries.SingleAsync(item => item.Action == "invite.resend");
            Assert.Contains("does not email", resent.Summary, StringComparison.OrdinalIgnoreCase);
            await service.RevokeInviteAsync(world.InviteId);
            Assert.NotNull((await db.Invitations.IgnoreQueryFilters().SingleAsync()).RevokedAt);

            var actions = await db.AdminAuditEntries.Select(item => item.Action).ToListAsync();
            Assert.Contains("businesses.list", actions);
            Assert.Contains("business.view", actions);
            Assert.Contains("business.rename", actions);
            Assert.Contains("business.disable", actions);
            Assert.Contains("user.remove", actions);
            Assert.Contains("user.disable", actions);
            Assert.Contains("invite.revoke", actions);
        }
    }

    [Fact]
    public async Task The_bypass_is_explicit_and_must_match_the_signed_in_person()
    {
        var (db, _, world) = await OpenWorldAsync();
        await using (db)
        {
            var bakery = await db.Organizations.IgnoreQueryFilters().SingleAsync(item => item.Id == world.BakeryId);
            bakery.Name = "Hijacked";
            var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Contains("different business", blocked.Message, StringComparison.OrdinalIgnoreCase);
            db.ChangeTracker.Clear();

            using (AdminDataAccess.Open(Guid.NewGuid()))
            {
                var again = await db.Organizations.IgnoreQueryFilters().SingleAsync(item => item.Id == world.BakeryId);
                again.Name = "Still hijacked";
                var mismatch = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
                Assert.Contains("does not match", mismatch.Message, StringComparison.OrdinalIgnoreCase);
            }

            db.ChangeTracker.Clear();
            Assert.Equal("Bakery Only", (await db.Organizations.IgnoreQueryFilters().SingleAsync(item => item.Id == world.BakeryId)).Name);

            using (AdminDataAccess.Open(world.Admin.UserId))
            {
                var allowed = await db.Organizations.IgnoreQueryFilters().SingleAsync(item => item.Id == world.BakeryId);
                allowed.Name = "Bakery Renamed";
                await db.SaveChangesAsync();
            }

            Assert.Equal("Bakery Renamed", (await db.Organizations.IgnoreQueryFilters().SingleAsync(item => item.Id == world.BakeryId)).Name);
            Assert.Equal(new[] { world.StudioId }, await db.Organizations.Select(item => item.Id).ToListAsync());
        }
    }

    [Fact]
    public async Task Delete_requires_the_typed_name_and_removes_every_business_table()
    {
        var (db, service, world) = await OpenWorldAsync();
        await using (db)
        {
            var wrong = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.DeleteBusinessAsync(world.StudioId, "studio books"));
            Assert.Contains("exactly", wrong.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, await db.Organizations.IgnoreQueryFilters().CountAsync());

            await service.DeleteBusinessAsync(world.StudioId, " Studio Books ");

            Assert.Equal(new[] { world.BakeryId }, await db.Organizations.IgnoreQueryFilters().Select(item => item.Id).ToListAsync());
            Assert.Equal(new[] { "Bakery customer" }, await db.Customers.IgnoreQueryFilters().Select(item => item.Name).ToListAsync());
            Assert.Empty(await db.Memberships.IgnoreQueryFilters().Where(item => item.OrganizationId == world.StudioId).ToListAsync());
            Assert.Empty(await db.ImportJobs.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.ColumnMappingProfiles.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.Transactions.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.WhyAnswers.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.WhyCitations.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.MorningBriefPreferences.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.MorningBriefs.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.MorningBriefCitations.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.DataSources.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.Invoices.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.InvoiceLines.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.Payments.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.CreditNotes.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.Invitations.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.CallThresholdSettings.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.CallCustomerActions.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.ReminderDrafts.IgnoreQueryFilters().ToListAsync());
            Assert.Equal(3, await db.Users.CountAsync());
            Assert.Contains(world.Storage.Deleted, path => path == world.StoragePath);

            var audit = await db.AdminAuditEntries.SingleAsync(item => item.Action == "business.delete");
            Assert.Equal(world.StudioId, audit.OrganizationId);
            Assert.Equal(world.Admin.Email, audit.ActorEmail);
        }
    }

    [Fact]
    public async Task Export_includes_every_business_table_and_nothing_from_another_business()
    {
        var (db, service, world) = await OpenWorldAsync();
        await using (db)
        {
            var export = await service.ExportBusinessAsync(world.StudioId);
            Assert.StartsWith("vhona-StudioBooks-", export.FileName, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(export.Content);
            var root = document.RootElement;
            foreach (var table in new[]
            {
                "memberships", "importJobs", "columnMappingProfiles", "transactions", "whyAnswers", "whyCitations",
                "morningBriefPreferences", "morningBriefs", "morningBriefCitations", "dataSources", "customers",
                "invoices", "invoiceLines", "payments", "creditNotes", "invitations", "callThresholdSettings",
                "callActions", "reminderDrafts"
            })
            {
                Assert.True(root.TryGetProperty(table, out var rows), table);
                Assert.True(rows.GetArrayLength() >= 1, table);
            }

            Assert.Equal("Studio Books", root.GetProperty("organization").GetProperty("Name").GetString());
            Assert.DoesNotContain("Bakery customer", root.ToString());
            Assert.Contains(
                await db.AdminAuditEntries.Select(item => item.Action).ToListAsync(),
                action => action == "business.export");
        }
    }

    [Fact]
    public async Task Deleting_an_import_removes_its_rows_and_file_and_keeps_the_customer()
    {
        var (db, service, world) = await OpenWorldAsync();
        await using (db)
        {
            var inspected = await service.GetImportAsync(world.ImportId);
            Assert.Equal(world.StudioId, inspected.OrganizationId);
            Assert.Single(inspected.Transactions);
            Assert.Single(inspected.Invoices);
            Assert.False(inspected.Truncated);

            await service.DeleteImportAsync(world.ImportId);

            var customer = await db.Customers.IgnoreQueryFilters().SingleAsync(item => item.OrganizationId == world.StudioId);
            Assert.Equal("Lerato Moyo", customer.Name);
            Assert.Null(customer.DataSourceId);
            Assert.Empty(await db.Invoices.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.InvoiceLines.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.Payments.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.CreditNotes.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.Transactions.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.DataSources.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.ImportJobs.IgnoreQueryFilters().ToListAsync());
            Assert.Contains(world.Storage.Deleted, path => path == world.StoragePath);
            Assert.Equal("Bakery customer", (await db.Customers.IgnoreQueryFilters().SingleAsync(item => item.OrganizationId == world.BakeryId)).Name);
            Assert.Contains(
                await db.AdminAuditEntries.Select(item => item.Action).ToListAsync(),
                action => action == "import.delete");
        }
    }

    [Fact]
    public async Task Global_defaults_apply_until_a_business_saves_its_own_and_invalid_values_are_rejected()
    {
        var (db, service, world) = await OpenWorldAsync(includeBusinessThresholds: false);
        await using (db)
        {
            var invalid = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UpdateGlobalDefaultsAsync(new CallThresholdSettingsUpdate(0, 150m, 2, 3)));
            Assert.Contains("Missed cycles", invalid.Message);
            Assert.Equal(0, await db.PlatformCallDefaults.CountAsync());

            var saved = await service.UpdateGlobalDefaultsAsync(new CallThresholdSettingsUpdate(4, 25m, 3, 5));
            Assert.True(saved.IsCustom);
            var read = await new CallSettingsService(db, world.Admin).GetAsync();
            Assert.False(read.IsCustom);
            Assert.Equal(4, read.StoppedMissedCycles);
            Assert.Equal(25m, read.DroppedPercent);
            Assert.Equal(3, read.DroppedMonths);
            Assert.Equal(5, read.MinimumInvoiceHistory);

            var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UpdateThresholdsAsync(world.StudioId, new CallThresholdSettingsUpdate(2, 50m, 2, 0)));
            Assert.Contains("history", rejected.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, await db.CallThresholdSettings.IgnoreQueryFilters().CountAsync());

            var custom = await service.UpdateThresholdsAsync(world.StudioId, new CallThresholdSettingsUpdate(6, 40m, 2, 4));
            Assert.True(custom.IsCustom);
            Assert.Equal(6, (await new CallSettingsService(db, world.Admin).GetAsync()).StoppedMissedCycles);
        }
    }

    [Fact]
    public async Task Partner_events_across_businesses_are_an_admin_view()
    {
        var (db, service, world) = await OpenWorldAsync();
        await using (db)
        {
            world.Events.Append(new AnalyticsEvent
            {
                Name = "finishes_upload",
                OrgId = world.StudioId,
                UserId = world.Admin.UserId,
                Timestamp = DateTime.UtcNow
            });
            world.Events.Append(new AnalyticsEvent
            {
                Name = "receipt_open",
                OrgId = world.BakeryId,
                UserId = world.MemberId,
                Timestamp = DateTime.UtcNow
            });

            var rows = await service.ListPartnerEventsAsync();
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, row => row.OrgId == world.BakeryId && row.Name == "receipt_open");
            Assert.Contains(
                await db.AdminAuditEntries.Select(item => item.Action).ToListAsync(),
                action => action == "partner-events.view");
        }
    }

    [Fact]
    public void Only_the_admin_console_opens_the_tenant_bypass()
    {
        var root = RepoRoot();
        var hits = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}VhonaAI.Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && File.ReadAllText(path).Contains("AdminDataAccess.Open(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name)
            .ToList();

        Assert.Equal(new[] { "AdminConsoleService.cs" }, hits);
    }

    private static object? ArgumentFor(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        if (type == typeof(CancellationToken))
        {
            return CancellationToken.None;
        }

        if (type == typeof(string))
        {
            return "probe";
        }

        if (type == typeof(Guid))
        {
            return Guid.NewGuid();
        }

        if (type == typeof(bool))
        {
            return false;
        }

        if (type == typeof(int))
        {
            return 20;
        }

        if (type == typeof(CallThresholdSettingsUpdate))
        {
            return new CallThresholdSettingsUpdate(2, 50m, 2, 3);
        }

        throw new InvalidOperationException($"No probe value for {type.Name}.");
    }

    private static async Task<(VhonaDbContext Db, AdminConsoleService Service, World World)> OpenWorldAsync(bool includeBusinessThresholds = true)
    {
        var admin = new Person(isAdmin: true, Guid.NewGuid());
        var storage = new MemoryStorage();
        var events = new MemoryEvents();
        var options = SqliteOptions();
        var world = await SeedAsync(options, admin, storage, events, includeBusinessThresholds);
        var db = new VhonaDbContext(options, admin);
        return (db, new AdminConsoleService(db, admin, storage, events), world);
    }

    private static async Task<(VhonaDbContext Db, AdminConsoleService Service, World World)> OpenAsync(bool isAdmin)
    {
        var admin = new Person(isAdmin, Guid.NewGuid());
        var storage = new MemoryStorage();
        var events = new MemoryEvents();
        var options = SqliteOptions();
        await using (var setup = new VhonaDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var db = new VhonaDbContext(options, admin);
        var world = new World(admin.OrganizationId, Guid.NewGuid(), admin, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "token", "none", storage, events);
        return (db, new AdminConsoleService(db, admin, storage, events), world);
    }

    private static async Task<World> SeedAsync(
        DbContextOptions<VhonaDbContext> options,
        Person admin,
        MemoryStorage storage,
        MemoryEvents events,
        bool includeBusinessThresholds)
    {
        var studioId = admin.OrganizationId;
        var bakeryId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var bakeryUserId = Guid.NewGuid();
        var ownerMembershipId = Guid.NewGuid();
        var memberMembershipId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var whyId = Guid.NewGuid();
        var briefId = Guid.NewGuid();
        var inviteId = Guid.NewGuid();
        const string inviteToken = "invite-token";
        const string storagePath = "uploads/studio/debtors.csv";
        var now = DateTime.UtcNow;

        await using var db = new VhonaDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Organizations.AddRange(
            new Organization { Id = studioId, Name = "Studio Books", CreatedAt = now },
            new Organization { Id = bakeryId, Name = "Bakery Only", CreatedAt = now });
        db.Users.AddRange(
            new AppUser { Id = admin.UserId, ExternalId = "ext-founder", Email = admin.Email, DisplayName = admin.DisplayName, CreatedAt = now },
            new AppUser { Id = memberId, ExternalId = "ext-member", Email = "member@vhona.local", DisplayName = "Member", CreatedAt = now },
            new AppUser { Id = bakeryUserId, ExternalId = "ext-bakery", Email = "bakery@vhona.local", DisplayName = "Baker", CreatedAt = now });
        db.Memberships.AddRange(
            new Membership { Id = ownerMembershipId, OrganizationId = studioId, UserId = admin.UserId, Role = MembershipRole.Owner, CreatedAt = now },
            new Membership { Id = memberMembershipId, OrganizationId = studioId, UserId = memberId, Role = MembershipRole.Member, CreatedAt = now },
            new Membership { Id = Guid.NewGuid(), OrganizationId = bakeryId, UserId = bakeryUserId, Role = MembershipRole.Owner, CreatedAt = now });
        db.ImportJobs.Add(new ImportJob
        {
            Id = importId,
            OrganizationId = studioId,
            CreatedByUserId = admin.UserId,
            OriginalFileName = "debtors.csv",
            StoragePath = storagePath,
            Status = ImportStatus.Imported,
            Kind = ImportKind.Invoices,
            ImportedRowCount = 1,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.ColumnMappingProfiles.Add(new ColumnMappingProfile
        {
            Id = Guid.NewGuid(),
            OrganizationId = studioId,
            Name = "Invoices",
            MappingJson = "{}",
            UpdatedAt = now
        });
        db.DataSources.Add(new DataSource
        {
            Id = sourceId,
            OrganizationId = studioId,
            Name = "debtors.csv",
            Kind = DataSourceKind.InvoiceCsv,
            ImportJobId = importId,
            CreatedAt = now
        });
        db.Customers.AddRange(
            new Customer
            {
                Id = customerId,
                OrganizationId = studioId,
                DataSourceId = sourceId,
                RowId = "cust_studio",
                Name = "Lerato Moyo",
                NormalizedName = "LERATO MOYO",
                CreatedAt = now,
                UpdatedAt = now
            },
            new Customer
            {
                Id = Guid.NewGuid(),
                OrganizationId = bakeryId,
                RowId = "cust_bakery",
                Name = "Bakery customer",
                NormalizedName = "BAKERY CUSTOMER",
                CreatedAt = now,
                UpdatedAt = now
            });
        db.Invoices.Add(new Invoice
        {
            Id = invoiceId,
            OrganizationId = studioId,
            CustomerId = customerId,
            DataSourceId = sourceId,
            ImportJobId = importId,
            RowId = "inv_1",
            Number = "INV-1",
            InvoiceDate = new DateOnly(2026, 10, 1),
            Amount = 100m,
            AmountDue = 40m,
            Status = InvoiceStatus.Open,
            Currency = "ZAR",
            SourceRowNumber = 2,
            ImportedAt = now
        });
        db.InvoiceLines.Add(new InvoiceLine
        {
            Id = Guid.NewGuid(),
            OrganizationId = studioId,
            InvoiceId = invoiceId,
            RowId = "line_1",
            LineNumber = 1,
            Amount = 100m
        });
        db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = studioId,
            InvoiceId = invoiceId,
            CustomerId = customerId,
            DataSourceId = sourceId,
            ImportJobId = importId,
            RowId = "pay_1",
            PaidDate = new DateOnly(2026, 10, 2),
            Amount = 60m,
            SourceRowNumber = 2,
            ImportedAt = now
        });
        db.CreditNotes.Add(new CreditNote
        {
            Id = Guid.NewGuid(),
            OrganizationId = studioId,
            InvoiceId = invoiceId,
            CustomerId = customerId,
            DataSourceId = sourceId,
            ImportJobId = importId,
            RowId = "cn_1",
            IssuedDate = new DateOnly(2026, 10, 3),
            Amount = 10m,
            SourceRowNumber = 2,
            ImportedAt = now
        });
        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            OrganizationId = studioId,
            ImportJobId = importId,
            DataSourceId = sourceId,
            RowId = "imp_1",
            SourceRowNumber = 2,
            Date = new DateOnly(2026, 10, 1),
            Description = "Invoice payment",
            Amount = 60m,
            Currency = "ZAR",
            ImportedAt = now
        });
        db.WhyAnswers.Add(new WhyAnswer
        {
            Id = whyId,
            OrganizationId = studioId,
            Question = "Why did profit drop?",
            Answer = "One invoice is still open.",
            CreatedAt = now
        });
        db.WhyCitations.Add(new WhyCitation { Id = Guid.NewGuid(), WhyAnswerId = whyId, RowId = "inv_1", Columns = "Amount" });
        db.MorningBriefPreferences.Add(new MorningBriefPreference
        {
            Id = Guid.NewGuid(),
            OrganizationId = studioId,
            UserId = admin.UserId,
            OptedIn = true
        });
        db.MorningBriefs.Add(new MorningBrief
        {
            Id = briefId,
            OrganizationId = studioId,
            UserId = admin.UserId,
            BriefDate = new DateOnly(2026, 10, 7),
            SnapshotJson = "{}",
            Explanation = "One invoice is open.",
            WhyAnswerId = whyId,
            CreatedAt = now
        });
        db.MorningBriefCitations.Add(new MorningBriefCitation
        {
            Id = Guid.NewGuid(),
            MorningBriefId = briefId,
            RowId = "inv_1",
            Columns = "Amount"
        });
        db.Invitations.Add(new Invitation
        {
            Id = inviteId,
            OrganizationId = studioId,
            Email = "new@vhona.local",
            Token = inviteToken,
            Role = MembershipRole.Member,
            CreatedByUserId = admin.UserId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(1)
        });
        db.CallCustomerActions.Add(new CallCustomerAction
        {
            Id = Guid.NewGuid(),
            OrganizationId = studioId,
            CustomerId = customerId,
            Kind = CallActionKind.Called,
            ActedByUserId = admin.UserId,
            At = now,
            Note = "Left a message"
        });
        db.ReminderDrafts.Add(new ReminderDraft
        {
            Id = Guid.NewGuid(),
            OrganizationId = studioId,
            CustomerId = customerId,
            Body = "A reminder that was not sent.",
            EditedByOwner = true,
            UpdatedAt = now,
            Status = ReminderDraftTemplate.NotSent
        });
        if (includeBusinessThresholds)
        {
            db.CallThresholdSettings.Add(new CallThresholdSettings
            {
                Id = Guid.NewGuid(),
                OrganizationId = studioId,
                StoppedMissedCycles = CallThresholdDefaults.StoppedMissedCycles,
                DroppedPercent = CallThresholdDefaults.DroppedPercent,
                DroppedMonths = CallThresholdDefaults.DroppedMonths,
                MinimumInvoiceHistory = CallThresholdDefaults.MinimumInvoiceHistory,
                UpdatedAt = now,
                UpdatedByUserId = admin.UserId
            });
        }

        await db.SaveChangesAsync();
        return new World(
            studioId,
            bakeryId,
            admin,
            memberId,
            ownerMembershipId,
            memberMembershipId,
            importId,
            inviteId,
            inviteToken,
            storagePath,
            storage,
            events);
    }

    private static DbContextOptions<VhonaDbContext> SqliteOptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "vhonaai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new DbContextOptionsBuilder<VhonaDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, "test.db")}")
            .Options;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "VhonaAI.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }

    private sealed record World(
        Guid StudioId,
        Guid BakeryId,
        Person Admin,
        Guid MemberId,
        Guid OwnerMembershipId,
        Guid MemberMembershipId,
        Guid ImportId,
        Guid InviteId,
        string InviteToken,
        string StoragePath,
        MemoryStorage Storage,
        MemoryEvents Events);

    private sealed class Person : ICurrentUser, IVhonaAdmin
    {
        public Person(bool isAdmin, Guid organizationId)
        {
            IsAdmin = isAdmin;
            OrganizationId = organizationId;
            UserId = Guid.NewGuid();
        }

        public bool IsAdmin { get; }
        public bool IsAuthenticated => true;
        public Guid UserId { get; }
        public Guid OrganizationId { get; }
        public string Email => "founder@vhona.local";
        public string DisplayName => "Founder";
        public string OrganizationName => "Studio Books";
        public string Role => "Owner";
        public string? ObjectId => "oid-founder";
    }

    private sealed class MemoryStorage : IFileStorage
    {
        public List<string> Deleted { get; } = new();

        public Task<string> SaveAsync(Stream content, Guid organizationId, Guid importJobId, string originalFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult("unused");

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            Deleted.Add(storagePath);
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryEvents : IEventLog
    {
        private readonly List<AnalyticsEvent> _events = new();

        public void Append(AnalyticsEvent evt) => _events.Add(evt);

        public IReadOnlyList<AnalyticsEvent> Read() => _events;
    }
}
