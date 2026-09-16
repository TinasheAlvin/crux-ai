using System.Globalization;
using CruxAI.Core.Identity;
using CruxAI.Infrastructure;
using CruxAI.Infrastructure.Data;
using CruxAI.Web.Components;
using CruxAI.Web.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;

var za = CultureInfo.GetCultureInfo("en-ZA");
CultureInfo.DefaultThreadCurrentCulture = za;
CultureInfo.DefaultThreadCurrentUICulture = za;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DemoAuthOptions>(builder.Configuration.GetSection(DemoAuthOptions.SectionName));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 11 * 1024 * 1024);

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ICurrentUser, ClaimsCurrentUser>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "cruxai.auth";
        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddCruxInfrastructure(builder.Configuration);

/*
 * TODO(auth): Replace the demo cookie bootstrap with Microsoft Entra External ID.
 *
 * 1. Add package Microsoft.Identity.Web
 * 2. Store TenantId / ClientId / ClientSecret in user-secrets (never commit secrets)
 * 3. Swap the authentication block above for:
 *
 *    builder.Services
 *        .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
 *        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("Auth:EntraExternalId"));
 *
 * 4. After sign-in, map the Entra oid + email onto AppUser.ExternalId and ensure a Membership
 *    for the chosen organisation (Org + User membership already exists in the data model).
 *
 * Config placeholders live in appsettings.json → Auth:EntraExternalId.
 */

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CruxDbContext>();
    var sqlitePath = builder.Configuration.GetConnectionString("Sqlite");
    if (!string.IsNullOrWhiteSpace(sqlitePath) && sqlitePath.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
    {
        var file = sqlitePath.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase).Trim();
        var directory = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    await db.Database.EnsureCreatedAsync();
    await SqliteSchemaPatches.ApplyAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapDemoAuthEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
