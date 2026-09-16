using CruxAI.Core.Csv;
using CruxAI.Core.Entities;
using CruxAI.Core.Storage;
using CruxAI.Core.Time;
using CruxAI.Core.Why;
using CruxAI.Infrastructure.Brief;
using CruxAI.Infrastructure.Csv;
using CruxAI.Infrastructure.Data;
using CruxAI.Infrastructure.Health;
using CruxAI.Infrastructure.Imports;
using CruxAI.Infrastructure.Storage;
using CruxAI.Infrastructure.Why;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CruxAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCruxInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var databaseProvider = configuration["Database:Provider"] ?? "Sqlite";
        services.AddDbContext<CruxDbContext>(options =>
        {
            if (string.Equals(databaseProvider, "AzureSql", StringComparison.OrdinalIgnoreCase))
            {
                var azureSql = configuration.GetConnectionString("AzureSql")
                    ?? throw new InvalidOperationException(
                        "Database:Provider is AzureSql but ConnectionStrings:AzureSql is empty. Store it in user-secrets.");
                options.UseSqlServer(azureSql);
            }
            else
            {
                var sqlite = configuration.GetConnectionString("Sqlite")
                    ?? "Data Source=App_Data/cruxai.db";
                var dataSource = sqlite.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (!Path.IsPathRooted(dataSource) && !dataSource.Contains(':'))
                {
                    var full = Path.GetFullPath(dataSource);
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                }

                options.UseSqlite(sqlite);
            }
        });

        var storageProvider = configuration["Storage:Provider"] ?? "Local";
        if (string.Equals(storageProvider, "AzureBlob", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFileStorage, AzureBlobFileStorage>();
        }
        else
        {
            var root = configuration["Storage:LocalRoot"] ?? "App_Data/uploads";
            services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(root));
        }

        services.AddSingleton<IOptions<AzureOpenAIOptions>>(_ =>
            Microsoft.Extensions.Options.Options.Create(new AzureOpenAIOptions
            {
                Endpoint = configuration["AzureOpenAI:Endpoint"] ?? string.Empty,
                DeploymentName = configuration["AzureOpenAI:DeploymentName"] ?? string.Empty,
                ApiKey = configuration["AzureOpenAI:ApiKey"] ?? string.Empty,
                ApiVersion = configuration["AzureOpenAI:ApiVersion"] ?? "2024-10-21"
            }));
        services.AddSingleton<ICsvReader, CsvHelperReader>();
        services.AddSingleton<IAzureOpenAIIntentClassifier, AzureOpenAIIntentClassifier>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddCruxAnalytics(configuration);
        services.AddScoped<ImportService>();
        services.AddScoped<HealthKpiService>();
        services.AddScoped<WhyService>();
        services.AddScoped<MorningBriefService>();
        services.AddScoped<OrgBootstrapper>();

        return services;
    }
}

public sealed class OrgBootstrapper
{
    private readonly CruxDbContext _db;

    public OrgBootstrapper(CruxDbContext db)
    {
        _db = db;
    }

    public async Task<(AppUser User, Organization Organization, Membership Membership)> EnsureDemoTenantAsync(
        string email,
        string displayName,
        string organizationName,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                ExternalId = $"demo:{email}",
                Email = email,
                DisplayName = displayName,
                CreatedAt = DateTime.UtcNow
            };
            _db.Users.Add(user);
        }
        else
        {
            user.DisplayName = displayName;
        }

        var membership = await _db.Memberships
            .Include(m => m.Organization)
            .FirstOrDefaultAsync(m => m.UserId == user.Id, cancellationToken);

        Organization organization;
        if (membership is null)
        {
            organization = new Organization
            {
                Id = Guid.NewGuid(),
                Name = organizationName,
                CreatedAt = DateTime.UtcNow
            };
            membership = new Membership
            {
                Id = Guid.NewGuid(),
                OrganizationId = organization.Id,
                UserId = user.Id,
                Role = MembershipRole.Owner,
                CreatedAt = DateTime.UtcNow
            };
            _db.Organizations.Add(organization);
            _db.Memberships.Add(membership);
        }
        else
        {
            organization = membership.Organization;
            organization.Name = organizationName;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return (user, organization, membership);
    }
}
