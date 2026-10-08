using VhonaAI.Application.Business;
using VhonaAI.Core.Entities;
using VhonaAI.Core.Identity;
using VhonaAI.Infrastructure;
using VhonaAI.Infrastructure.Data;
using VhonaAI.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace VhonaAI.Tests;

public class BusinessOnboardingTests
{
    [Fact]
    public async Task Entra_sign_in_does_not_create_or_rename_a_business()
    {
        await using var db = await CreateDbAsync();
        var bootstrapper = new OrgBootstrapper(db);

        var user = await bootstrapper.UpsertExternalUserAsync(
            "oid-ada",
            "ada@example.com",
            "Ada",
            CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.Organizations.CountAsync());
        Assert.Null(await bootstrapper.FindMembershipAsync(user.Id));
        Assert.DoesNotContain(await db.Organizations.Select(item => item.Name).ToListAsync(), name => name == "Harbour Street Studio");
    }

    [Fact]
    public async Task Empty_business_name_does_not_fall_back_to_harbour_street()
    {
        await using var db = await CreateDbAsync();
        var bootstrapper = new OrgBootstrapper(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bootstrapper.EnsureExternalUserAsync("oid-ada", "ada@example.com", "Ada", "   "));

        Assert.Contains("business name", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await db.Organizations.CountAsync());
    }

    [Fact]
    public async Task Second_sign_in_keeps_the_name_the_owner_chose()
    {
        await using var db = await CreateDbAsync();
        var bootstrapper = new OrgBootstrapper(db);

        await bootstrapper.EnsureExternalUserAsync("oid-ada", "ada@example.com", "Ada", "Ada Studio");
        var second = await bootstrapper.EnsureExternalUserAsync("oid-ada", "ada@example.com", "Ada", "Renamed Studio");

        Assert.Equal("Ada Studio", second.Organization.Name);
        Assert.Equal("Ada Studio", await db.Organizations.Select(item => item.Name).SingleAsync());
    }

    [Fact]
    public async Task A_new_demo_owner_has_no_business_and_stays_reachable()
    {
        await using var db = await CreateDbAsync();
        var bootstrapper = new OrgBootstrapper(db);

        var first = await bootstrapper.EnsureDemoUserWithoutBusinessAsync(
            "newowner@harbourstreet.local",
            "New Owner");

        Assert.Equal("newowner@harbourstreet.local", first.Email);
        Assert.Null(await bootstrapper.FindMembershipAsync(first.Id));
        Assert.Equal(0, await db.Organizations.CountAsync());

        var again = await bootstrapper.EnsureDemoUserWithoutBusinessAsync(
            "newowner@harbourstreet.local",
            "New Owner");
        Assert.Equal(first.Id, again.Id);

        db.Organizations.Add(new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Named Studio",
            CreatedAt = DateTime.UtcNow
        });
        db.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid(),
            OrganizationId = db.Organizations.Local.Single().Id,
            UserId = first.Id,
            Role = MembershipRole.Owner,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var fresh = await bootstrapper.EnsureDemoUserWithoutBusinessAsync(
            "newowner@harbourstreet.local",
            "New Owner");

        Assert.NotEqual(first.Id, fresh.Id);
        Assert.Null(await bootstrapper.FindMembershipAsync(fresh.Id));
        Assert.StartsWith("newowner+", fresh.Email);
    }

    [Fact]
    public async Task Demo_sign_in_does_not_rename_an_existing_business()
    {
        await using var db = await CreateDbAsync();
        var bootstrapper = new OrgBootstrapper(db);

        await bootstrapper.EnsureDemoTenantAsync("owner@harbourstreet.local", "Demo Owner", "Harbour Street Studio");
        var second = await bootstrapper.EnsureDemoTenantAsync("owner@harbourstreet.local", "Demo Owner", "Other Studio");

        Assert.Equal("Harbour Street Studio", second.Organization.Name);
    }

    [Fact]
    public async Task Owner_invites_a_member_and_a_member_cannot_administer()
    {
        await using var db = await CreateDbAsync();
        var owner = new MutableUser { Email = "ada@example.com", DisplayName = "Ada" };
        var ownerUser = new AppUser
        {
            Id = owner.UserId,
            Email = owner.Email,
            DisplayName = owner.DisplayName,
            ExternalId = "oid-ada",
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(ownerUser);
        await db.SaveChangesAsync();

        var businesses = new BusinessService(db, owner);
        var created = await businesses.CreateBusinessAsync(owner.UserId, "Ada Studio");
        owner.OrganizationId = created.OrganizationId;
        owner.OrganizationName = created.OrganizationName;
        owner.Role = "Owner";

        var invite = await businesses.InviteMemberAsync("Member@Example.com");
        Assert.Contains("@", invite.Email);
        Assert.Equal("member@example.com", invite.Email);
        Assert.True(invite.Token.Length >= 32);
        Assert.True(invite.ExpiresAt > DateTime.UtcNow.AddDays(13));

        var member = new MutableUser { Email = "member@example.com", DisplayName = "Mo" };
        db.Users.Add(new AppUser
        {
            Id = member.UserId,
            Email = member.Email,
            DisplayName = member.DisplayName,
            ExternalId = "oid-mo",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var memberBusiness = new BusinessService(db, member);
        var pending = await memberBusiness.ListInvitesForCurrentEmailAsync();
        Assert.Equal(invite.Token, pending.Single().Token);

        var accepted = await memberBusiness.AcceptInviteAsync(invite.Token);
        Assert.Equal("Member", accepted.Role);
        Assert.Equal(created.OrganizationId, accepted.OrganizationId);

        member.OrganizationId = accepted.OrganizationId;
        member.OrganizationName = accepted.OrganizationName;
        member.Role = accepted.Role;

        var rename = await Assert.ThrowsAsync<InvalidOperationException>(() => memberBusiness.RenameAsync("Taken Over"));
        Assert.Equal("Only an owner can do that.", rename.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => memberBusiness.InviteMemberAsync("other@example.com"));
        Assert.Equal("Ada Studio", await db.Organizations.IgnoreQueryFilters().Select(item => item.Name).SingleAsync());

        var members = await memberBusiness.ListMembersAsync();
        Assert.Equal(2, members.Count);
        Assert.Contains(members, item => item.Role == "Owner" && item.Email == "ada@example.com");
        Assert.Contains(members, item => item.Role == "Member" && item.Email == "member@example.com");
    }

    [Fact]
    public async Task Invite_cannot_be_accepted_by_someone_who_already_has_a_business()
    {
        await using var db = await CreateDbAsync();
        var owner = await SeedOwnerAsync(db, "ada@example.com");
        var businesses = new BusinessService(db, owner.User);
        var created = await businesses.CreateBusinessAsync(owner.User.UserId, "Ada Studio");
        owner.User.Bind(created);

        var invite = await businesses.InviteMemberAsync("mo@example.com");
        var member = await SeedOwnerAsync(db, "mo@example.com");
        var memberBusiness = new BusinessService(db, member.User);
        var own = await memberBusiness.CreateBusinessAsync(member.User.UserId, "Mo Studio");
        member.User.Bind(own);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => memberBusiness.AcceptInviteAsync(invite.Token));
        Assert.Contains("already belongs", ex.Message);
    }

    [Fact]
    public async Task Revoked_and_mismatched_invites_are_rejected()
    {
        await using var db = await CreateDbAsync();
        var owner = await SeedOwnerAsync(db, "ada@example.com");
        var businesses = new BusinessService(db, owner.User);
        owner.User.Bind(await businesses.CreateBusinessAsync(owner.User.UserId, "Ada Studio"));
        var invite = await businesses.InviteMemberAsync("mo@example.com");

        var stranger = new MutableUser { Email = "other@example.com", DisplayName = "Other" };
        db.Users.Add(User(stranger));
        await db.SaveChangesAsync();
        var strangerBusiness = new BusinessService(db, stranger);
        var mismatch = await Assert.ThrowsAsync<InvalidOperationException>(() => strangerBusiness.AcceptInviteAsync(invite.Token));
        Assert.Contains("different email", mismatch.Message);

        await businesses.RevokeInviteAsync(invite.Id);
        var member = new MutableUser { Email = "mo@example.com", DisplayName = "Mo" };
        db.Users.Add(User(member));
        await db.SaveChangesAsync();
        var revoked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new BusinessService(db, member).AcceptInviteAsync(invite.Token));
        Assert.Contains("no longer valid", revoked.Message);
    }

    private static async Task<(MutableUser User, AppUser Account)> SeedOwnerAsync(VhonaDbContext db, string email)
    {
        var user = new MutableUser { Email = email, DisplayName = email, Role = "Owner" };
        var account = User(user);
        db.Users.Add(account);
        await db.SaveChangesAsync();
        return (user, account);
    }

    private static AppUser User(MutableUser user) =>
        new()
        {
            Id = user.UserId,
            Email = user.Email,
            DisplayName = user.DisplayName,
            ExternalId = "oid-" + user.UserId.ToString("N"),
            CreatedAt = DateTime.UtcNow
        };

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

    private sealed class MutableUser : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public Guid UserId { get; set; } = Guid.NewGuid();
        public Guid OrganizationId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string OrganizationName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;

        public void Bind(BusinessMembership membership)
        {
            OrganizationId = membership.OrganizationId;
            OrganizationName = membership.OrganizationName;
            Role = membership.Role;
        }
    }
}
