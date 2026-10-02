using VhonaAI.Infrastructure;
using VhonaAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Tests;

public class ExternalUserBootstrapTests
{
    [Fact]
    public async Task External_sign_in_creates_owner_membership_and_is_idempotent()
    {
        await using var db = await CreateDbAsync();
        var bootstrapper = new OrgBootstrapper(db);
        var oid = "11111111-1111-1111-1111-111111111111";

        var first = await bootstrapper.EnsureExternalUserAsync(
            oid,
            "owner@harbourstreet.example",
            "Demo Owner",
            "Harbour Street Studio");
        var second = await bootstrapper.EnsureExternalUserAsync(
            oid,
            "owner@harbourstreet.example",
            "Demo Owner",
            "Harbour Street Studio");

        Assert.Equal(first.User.Id, second.User.Id);
        Assert.Equal(first.Organization.Id, second.Organization.Id);
        Assert.Equal(oid, first.User.ExternalId);
        Assert.Equal("Owner", first.Membership.Role.ToString());
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(1, await db.Organizations.CountAsync());
        Assert.Equal(1, await db.Memberships.CountAsync());
    }

    [Fact]
    public async Task External_sign_in_rejects_an_email_already_used_by_the_demo_user()
    {
        await using var db = await CreateDbAsync();
        var bootstrapper = new OrgBootstrapper(db);
        await bootstrapper.EnsureDemoTenantAsync(
            "owner@harbourstreet.local",
            "Demo Owner",
            "Harbour Street Studio");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bootstrapper.EnsureExternalUserAsync(
                "11111111-1111-1111-1111-111111111111",
                "owner@harbourstreet.local",
                "Other",
                "Harbour Street Studio"));

        Assert.Contains("different sign-in", ex.Message);
    }

    [Fact]
    public async Task Demo_tenant_still_bootstraps_from_email()
    {
        await using var db = await CreateDbAsync();
        var bootstrapper = new OrgBootstrapper(db);
        var (user, organization, membership) = await bootstrapper.EnsureDemoTenantAsync(
            "owner@harbourstreet.local",
            "Demo Owner",
            "Harbour Street Studio");

        Assert.Equal("demo:owner@harbourstreet.local", user.ExternalId);
        Assert.Equal("Harbour Street Studio", organization.Name);
        Assert.Equal(user.Id, membership.UserId);
    }

    [Fact]
    public async Task Missing_email_fails_closed()
    {
        await using var db = await CreateDbAsync();
        var bootstrapper = new OrgBootstrapper(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bootstrapper.EnsureExternalUserAsync("oid-1", "   ", "Demo", "Harbour Street Studio"));
    }

    private static async Task<VhonaDbContext> CreateDbAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "vhonaai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var options = new DbContextOptionsBuilder<VhonaDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, "test.db")}")
            .Options;
        var db = new VhonaDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
