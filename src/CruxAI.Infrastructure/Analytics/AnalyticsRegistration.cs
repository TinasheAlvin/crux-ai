using CruxAI.Core.Analytics;
using CruxAI.Infrastructure.Analytics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CruxAI.Infrastructure;

public static class AnalyticsOptions
{
    public const string SectionName = "Analytics";
    public const string DefaultFilePath = "App_Data/partner-events.jsonl";
    public const string DefaultSqlitePath = "App_Data/partner-events.db";
}

public static class AnalyticsRegistration
{
    public static IServiceCollection AddCruxAnalytics(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var sink = configuration["Analytics:Sink"] ?? "File";
        if (string.Equals(sink, "Memory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEventLog, InMemoryEventLog>();
        }
        else if (string.Equals(sink, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            var path = configuration["Analytics:SqlitePath"];
            if (string.IsNullOrWhiteSpace(path))
            {
                path = AnalyticsOptions.DefaultSqlitePath;
            }

            services.AddSingleton<IEventLog>(_ => new SqliteEventLog(path));
        }
        else
        {
            var path = configuration["Analytics:FilePath"];
            if (string.IsNullOrWhiteSpace(path))
            {
                path = AnalyticsOptions.DefaultFilePath;
            }

            services.AddSingleton<IEventLog>(_ => new JsonlFileEventLog(path));
        }

        services.AddScoped<PartnerSession>();
        services.AddScoped<IAnalytics, AnalyticsService>();
        return services;
    }
}
