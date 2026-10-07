using VhonaAI.Application.Calling;
using VhonaAI.Core.Calling;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Identity;
using VhonaAI.Infrastructure.Calling;
using VhonaAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Tests;

public class CallSettingsTests
{
    [Fact]
    public async Task Defaults_are_returned_until_an_owner_saves_custom_values()
    {
        var (db, user) = await CreateAsync();
        await using (db)
        {
            var service = new CallSettingsService(db, user);
            var defaults = await service.GetAsync();
            Assert.False(defaults.IsCustom);
            Assert.Equal(CallThresholdDefaults.StoppedMissedCycles, defaults.StoppedMissedCycles);
            Assert.Equal(CallThresholdDefaults.DroppedPercent, defaults.DroppedPercent);
            Assert.Equal(CallThresholdDefaults.DroppedMonths, defaults.DroppedMonths);
            Assert.Equal(CallThresholdDefaults.MinimumInvoiceHistory, defaults.MinimumInvoiceHistory);

            var saved = await service.UpdateAsync(new CallThresholdSettingsUpdate(3, 40m, 4, 6));
            Assert.True(saved.IsCustom);
            Assert.Equal(3, saved.StoppedMissedCycles);
            Assert.Equal(40m, saved.DroppedPercent);
            Assert.Equal(4, saved.DroppedMonths);
            Assert.Equal(6, saved.MinimumInvoiceHistory);
            Assert.Equal(1, await db.CallThresholdSettings.CountAsync());
        }
    }

    [Fact]
    public async Task Invalid_thresholds_are_rejected()
    {
        var (db, user) = await CreateAsync();
        await using (db)
        {
            var service = new CallSettingsService(db, user);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UpdateAsync(new CallThresholdSettingsUpdate(0, 150m, 0, 0)));
            Assert.Contains("Missed cycles", ex.Message);
            Assert.Contains("Drop percent", ex.Message);
            Assert.Equal(0, await db.CallThresholdSettings.CountAsync());
        }
    }

    [Fact]
    public async Task A_member_can_read_thresholds_and_cannot_change_them()
    {
        var (db, owner) = await CreateAsync();
        await using (db)
        {
            var member = new FixedUser(owner.OrganizationId, "Member");
            var service = new CallSettingsService(db, member);
            var read = await service.GetAsync();
            Assert.False(read.IsCustom);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UpdateAsync(new CallThresholdSettingsUpdate(2, 50m, 2, 3)));
            Assert.Contains("owner", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<(VhonaDbContext Db, FixedUser User)> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "vhonaai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var options = new DbContextOptionsBuilder<VhonaDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, "test.db")}")
            .Options;
        var organizationId = Guid.NewGuid();
        var user = new FixedUser(organizationId, "Owner");
        var db = new VhonaDbContext(options, user);
        await db.Database.EnsureCreatedAsync();
        db.Organizations.Add(new Organization
        {
            Id = organizationId,
            Name = "Ada Studio",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (db, user);
    }

    private sealed class FixedUser : ICurrentUser
    {
        public FixedUser(Guid organizationId, string role)
        {
            OrganizationId = organizationId;
            Role = role;
        }

        public bool IsAuthenticated => true;
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid OrganizationId { get; }
        public string Email => "owner@example.com";
        public string DisplayName => "Owner";
        public string OrganizationName => "Ada Studio";
        public string Role { get; }
    }
}
