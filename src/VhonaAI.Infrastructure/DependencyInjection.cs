using VhonaAI.Application.Admin;
using VhonaAI.Application.Brief;
using VhonaAI.Application.Business;
using VhonaAI.Application.Calling;
using VhonaAI.Application.Health;
using VhonaAI.Application.Imports;
using VhonaAI.Application.Why;
using VhonaAI.Core.Csv;
using VhonaAI.Core.Storage;
using VhonaAI.Core.Time;
using VhonaAI.Core.Why;
using VhonaAI.Infrastructure.Admin;
using VhonaAI.Infrastructure.Brief;
using VhonaAI.Infrastructure.Calling;
using VhonaAI.Infrastructure.Csv;
using VhonaAI.Infrastructure.Data;
using VhonaAI.Infrastructure.Health;
using VhonaAI.Infrastructure.Hosting;
using VhonaAI.Infrastructure.Identity;
using VhonaAI.Infrastructure.Imports;
using VhonaAI.Infrastructure.Storage;
using VhonaAI.Infrastructure.Why;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace VhonaAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddVhonaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (HostingConfiguration.IsAzureSql(configuration))
        {
            var azureSql = HostingConfiguration.RequireAzureSqlConnectionString(configuration);
            services.AddDbContext<VhonaDbContext>(options =>
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
                ?? "Data Source=App_Data/vhonaai.db";
            services.AddDbContext<VhonaDbContext>(options =>
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

                options.UseVhonaSqlite(sqlite);
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
        services.AddVhonaAnalytics(configuration);
        services.AddScoped<ImportService>();
        services.AddScoped<IImportAppService>(sp => sp.GetRequiredService<ImportService>());
        services.AddScoped<HealthKpiService>();
        services.AddScoped<IHealthKpiAppService>(sp => sp.GetRequiredService<HealthKpiService>());
        services.AddScoped<WhyService>();
        services.AddScoped<IWhyAppService>(sp => sp.GetRequiredService<WhyService>());
        services.AddScoped<MorningBriefService>();
        services.AddScoped<IMorningBriefAppService>(sp => sp.GetRequiredService<MorningBriefService>());
        services.AddScoped<BusinessService>();
        services.AddScoped<IBusinessAppService>(sp => sp.GetRequiredService<BusinessService>());
        services.AddScoped<CallSettingsService>();
        services.AddScoped<ICallSettingsAppService>(sp => sp.GetRequiredService<CallSettingsService>());
        services.AddScoped<WhoToCallService>();
        services.AddScoped<IWhoToCallAppService>(sp => sp.GetRequiredService<WhoToCallService>());
        services.AddScoped<OrgBootstrapper>();
        services.AddScoped<AdminConsoleService>();
        services.AddScoped<IAdminConsoleService>(sp => sp.GetRequiredService<AdminConsoleService>());

        return services;
    }
}
