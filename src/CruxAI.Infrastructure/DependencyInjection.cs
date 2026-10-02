using CruxAI.Core.Csv;
using CruxAI.Core.Storage;
using CruxAI.Core.Time;
using CruxAI.Core.Why;
using CruxAI.Infrastructure.Brief;
using CruxAI.Infrastructure.Csv;
using CruxAI.Infrastructure.Data;
using CruxAI.Infrastructure.Health;
using CruxAI.Infrastructure.Hosting;
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
        if (HostingConfiguration.IsAzureSql(configuration))
        {
            var azureSql = HostingConfiguration.RequireAzureSqlConnectionString(configuration);
            services.AddDbContext<CruxDbContext>(options =>
            {
                options.UseSqlServer(azureSql, sql =>
                {
                    sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                    sql.CommandTimeout(60);
                });
            });
        }
        else
        {
            var sqlite = configuration.GetConnectionString("Sqlite")
                ?? "Data Source=App_Data/cruxai.db";
            services.AddDbContext<CruxDbContext>(options =>
            {
                var dataSource = sqlite.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (!Path.IsPathRooted(dataSource) && !dataSource.Contains(':'))
                {
                    var full = Path.GetFullPath(dataSource);
                    var directory = Path.GetDirectoryName(full);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                }

                options.UseSqlite(sqlite);
            });
        }

        if (HostingConfiguration.IsAzureBlob(configuration))
        {
            HostingConfiguration.RequireAzureBlobConnectionString(configuration);
            services.AddSingleton<IFileStorage>(_ => AzureBlobFileStorage.FromConfiguration(configuration));
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
