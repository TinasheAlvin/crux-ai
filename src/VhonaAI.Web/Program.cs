using System.Globalization;
using System.Security.Claims;
using VhonaAI.Core.Analytics;
using VhonaAI.Core.Identity;
using VhonaAI.Infrastructure;
using VhonaAI.Infrastructure.Hosting;
using VhonaAI.Web.Components;
using VhonaAI.Web.Hosting;
using VhonaAI.Web.Identity;
using Microsoft.AspNetCore.Authentication;

var za = CultureInfo.GetCultureInfo("en-ZA");
CultureInfo.DefaultThreadCurrentCulture = za;
CultureInfo.DefaultThreadCurrentUICulture = za;

var builder = WebApplication.CreateBuilder(args);

builder.AddVhonaHosting();
builder.Services.Configure<DemoAuthOptions>(builder.Configuration.GetSection(DemoAuthOptions.SectionName));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 11 * 1024 * 1024);

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ICurrentUser, ClaimsCurrentUser>();
builder.AddVhonaAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();
builder.Services.AddVhonaInfrastructure(builder.Configuration);

var app = builder.Build();

await VhonaDatabaseStartup.InitializeAsync(app.Services, app.Configuration);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

if (VhonaWebHost.UseForwardedHeadersFor(app.Environment, app.Configuration))
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true
        && !Guid.TryParse(context.User.FindFirstValue("org_id"), out _)
        && !AllowsMissingOrganization(context.Request.Path))
    {
        context.Response.Redirect("/onboarding");
        return;
    }

    await next();
});

app.MapGet("/healthz", (IConfiguration configuration, IWebHostEnvironment environment) =>
{
    const string sampleName = "sample-transactions-clean.csv";
    var sampleCsv = new[]
    {
        Path.Combine(environment.ContentRootPath, "testdata", sampleName),
        Path.Combine(AppContext.BaseDirectory, "testdata", sampleName)
    }.Any(File.Exists);

    return Results.Json(new
    {
        status = "ok",
        database = configuration["Database:Provider"] ?? HostingConfiguration.SqliteProvider,
        storage = configuration["Storage:Provider"] ?? HostingConfiguration.LocalStorageProvider,
        auth = configuration["Auth:Provider"] ?? HostingConfiguration.DemoAuthProvider,
        sampleCsv
    });
}).AllowAnonymous();

app.MapVhonaAuthEndpoints();
app.MapBusinessAuthEndpoints();
app.MapGet("/internal/partner-events", (IEventLog log, ClaimsPrincipal user) =>
{
    var isAdmin = user.HasClaim("internal_admin", "true");
    Guid? organizationId = Guid.TryParse(user.FindFirstValue("org_id"), out var parsed) ? parsed : null;
    if (!PartnerEventAccess.TryFilter(log.Read(), organizationId, isAdmin, out var events, out var denial))
    {
        return Results.Json(new { error = denial }, statusCode: StatusCodes.Status403Forbidden);
    }

    return Results.Json(new
    {
        count = events.Count,
        events = events.Select(evt => new
        {
            evt.Name,
            evt.OrgId,
            evt.UserId,
            timestamp = evt.Timestamp,
            evt.Properties
        })
    });
}).RequireAuthorization();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool AllowsMissingOrganization(PathString path)
{
    var value = path.Value ?? string.Empty;
    if (value.StartsWith("/onboarding", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/join", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/auth", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/healthz", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/signin-oidc", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/signout-callback-oidc", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/_content", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return Path.HasExtension(value);
}
