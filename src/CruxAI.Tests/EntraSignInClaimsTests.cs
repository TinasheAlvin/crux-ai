using System.Security.Claims;
using CruxAI.Infrastructure.Identity;

namespace CruxAI.Tests;

public class EntraSignInClaimsTests
{
    [Fact]
    public void Read_prefers_oid_and_email_over_display_claims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("oid", "11111111-1111-1111-1111-111111111111"),
            new Claim(ClaimTypes.NameIdentifier, "not-the-oid"),
            new Claim("preferred_username", "owner@harbourstreet.example"),
            new Claim("name", "Demo Owner")
        ], "oidc"));

        var identity = EntraSignInClaims.Read(principal);
        Assert.Equal("11111111-1111-1111-1111-111111111111", identity.ExternalId);
        Assert.Equal("owner@harbourstreet.example", identity.Email);
        Assert.Equal("Demo Owner", identity.DisplayName);
    }

    [Fact]
    public void Read_accepts_a_json_emails_claim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("oid", "11111111-1111-1111-1111-111111111111"),
            new Claim("emails", "[\"owner@harbourstreet.example\"]")
        ], "oidc"));

        Assert.Equal("owner@harbourstreet.example", EntraSignInClaims.Read(principal).Email);
    }

    [Fact]
    public void Read_skips_a_non_email_preferred_username_when_emails_is_present()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("oid", "11111111-1111-1111-1111-111111111111"),
            new Claim("preferred_username", "owner"),
            new Claim("emails", "[\"owner@harbourstreet.example\"]")
        ], "oidc"));

        Assert.Equal("owner@harbourstreet.example", EntraSignInClaims.Read(principal).Email);
    }

    [Fact]
    public void Read_ignores_a_preferred_username_that_is_not_an_email()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("oid", "11111111-1111-1111-1111-111111111111"),
            new Claim("preferred_username", "owner")
        ], "oidc"));

        Assert.Null(EntraSignInClaims.Read(principal).Email);
    }

    [Fact]
    public void ApplyTenant_stamps_crux_claims_without_removing_the_oid()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("oid", "11111111-1111-1111-1111-111111111111"),
            new Claim(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111")
        ], "oidc");
        var userId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var orgId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        EntraSignInClaims.ApplyTenant(identity, userId, "owner@harbourstreet.example", "Demo Owner", orgId, "Harbour Street Studio", "Owner");

        Assert.Equal(userId.ToString(), identity.FindFirst("crux_user_id")!.Value);
        Assert.Equal(orgId.ToString(), identity.FindFirst("org_id")!.Value);
        Assert.Equal("Harbour Street Studio", identity.FindFirst("org_name")!.Value);
        Assert.Equal("Owner", identity.FindFirst("org_role")!.Value);
        Assert.Equal("owner@harbourstreet.example", identity.FindFirst(ClaimTypes.Email)!.Value);
        Assert.Equal("11111111-1111-1111-1111-111111111111", identity.FindFirst("oid")!.Value);
    }
}
