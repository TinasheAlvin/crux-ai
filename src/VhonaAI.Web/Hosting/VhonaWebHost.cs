using Azure.Storage.Blobs;
using VhonaAI.Infrastructure.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;

namespace VhonaAI.Web.Hosting;

public static class VhonaWebHost
{
    public static void AddVhonaHosting(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // App Service terminates TLS. The platform proxy is not a loopback address.
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        AddDataProtection(builder);
        AddTelemetry(builder);
    }

    public static bool UseForwardedHeadersFor(IWebHostEnvironment environment, IConfiguration configuration)
    {
        if (!environment.IsDevelopment())
        {
            return true;
        }

        return string.Equals(
            configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"],
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static void AddDataProtection(WebApplicationBuilder builder)
    {
        if (!HostingConfiguration.IsAzureBlob(builder.Configuration))
        {
            return;
        }

        var connectionString = HostingConfiguration.RequireAzureBlobConnectionString(builder.Configuration);
        var containerName = builder.Configuration["Storage:DataProtection:ContainerName"];
        if (string.IsNullOrWhiteSpace(containerName) || HostingConfiguration.IsUnset(containerName))
        {
            containerName = "vhona-keys";
        }

        try
        {
            var container = new BlobServiceClient(connectionString).GetBlobContainerClient(containerName);
            container.CreateIfNotExists();
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Could not open the Azure Blob container used for data-protection keys (" + containerName + "). " +
                "Check Storage:AzureBlob:ConnectionString and that the app can reach the storage account.",
                ex);
        }

        builder.Services.AddDataProtection()
            .SetApplicationName("vhona-ai")
            .PersistKeysToAzureBlobStorage(connectionString, containerName, "keys.xml");
    }

    private static void AddTelemetry(WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (HostingConfiguration.IsUnset(connectionString))
        {
            connectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
        }

        if (HostingConfiguration.IsUnset(connectionString)
            || HostingConfiguration.IsUnresolvedKeyVaultReference(connectionString))
        {
            return;
        }

        builder.Services.AddApplicationInsightsTelemetry(new ApplicationInsightsServiceOptions
        {
            ConnectionString = connectionString,
            EnableAdaptiveSampling = true
        });
    }
}
