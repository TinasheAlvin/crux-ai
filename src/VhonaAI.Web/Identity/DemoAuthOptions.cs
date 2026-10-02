namespace VhonaAI.Web.Identity;

public sealed class DemoAuthOptions
{
    public const string SectionName = "DemoAuth";

    public string Email { get; set; } = "owner@harbourstreet.local";
    public string DisplayName { get; set; } = "Demo Owner";
    public string OrganizationName { get; set; } = "Harbour Street Studio";
}
