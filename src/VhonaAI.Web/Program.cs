using System.Globalization;
using VhonaAI.Core.Analytics;
using VhonaAI.Core.Identity;
using VhonaAI.Infrastructure;
using VhonaAI.Infrastructure.Hosting;
using VhonaAI.Web.Components;
using VhonaAI.Web.Hosting;
using VhonaAI.Web.Identity;

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
app.MapGet("/internal/partner-events", (IEventLog log) =>
{
    var events = log.Read();
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
