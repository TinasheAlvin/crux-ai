using VhonaAI.Core.Storage;
using VhonaAI.Infrastructure;
using VhonaAI.Infrastructure.Hosting;
using VhonaAI.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace VhonaAI.Tests;

public class HostingConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<TODO-entra-external-id-tenant-id>")]
    [InlineData("YOUR_ACCOUNT")]
    [InlineData("Server=tcp:YOUR_SERVER.database.windows.net;User ID=;Password=;")]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=x;AccountKey=;EndpointSuffix=core.windows.net")]
    public void Placeholder_secrets_are_unset(string? value)
    {
        Assert.True(HostingConfiguration.IsUnset(value));
    }

    [Fact]
    public void Real_connection_string_is_set()
    {
        Assert.False(HostingConfiguration.IsUnset(
            "Server=tcp:vhona.database.windows.net,1433;Initial Catalog=vhonaai;User ID=vhonaadmin;Password=example-pass-1;Encrypt=True;"));
    }

    [Fact]
    public void Unresolved_key_vault_reference_is_rejected()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AzureSql"] = "@Microsoft.KeyVault(SecretUri=https://vhona.vault.azure.net/secrets/sql-connection/)"
        });

        var ex = Assert.Throws<InvalidOperationException>(() => HostingConfiguration.RequireAzureSqlConnectionString(config));
        Assert.Contains("Key Vault reference was not resolved", ex.Message);
    }

    [Fact]
    public void AzureSql_without_connection_string_fails_at_registration()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "AzureSql",
            ["ConnectionStrings:AzureSql"] = ""
        });

        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddVhonaInfrastructure(config));
        Assert.Contains("ConnectionStrings:AzureSql", ex.Message);
    }

    [Fact]
    public void AzureBlob_without_connection_string_fails_at_registration()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "AzureBlob",
            ["Storage:AzureBlob:ConnectionString"] = "DefaultEndpointsProtocol=https;AccountName=YOUR_ACCOUNT;AccountKey=;EndpointSuffix=core.windows.net"
        });

        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddVhonaInfrastructure(config));
        Assert.Contains("Storage:AzureBlob:ConnectionString", ex.Message);
    }

    [Fact]
    public void AzureBlob_provider_registers_blob_storage_without_calling_azure()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Sqlite"] = "Data Source=:memory:",
            ["Storage:Provider"] = "AzureBlob",
            ["Storage:AzureBlob:ConnectionString"] = "DefaultEndpointsProtocol=https;AccountName=vhonademo;AccountKey=ZmFrZQ==;EndpointSuffix=core.windows.net",
            ["Storage:AzureBlob:ContainerName"] = "csv-uploads",
            ["Analytics:Sink"] = "Memory"
        });

        var services = new ServiceCollection();
        services.AddVhonaInfrastructure(config);
        using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<IFileStorage>();
        Assert.IsType<AzureBlobFileStorage>(storage);
    }

    [Fact]
    public void Local_provider_stays_on_disk()
    {
        var root = Path.Combine(Path.GetTempPath(), "vhonaai-tests", Guid.NewGuid().ToString("N"));
        var config = Config(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Sqlite"] = "Data Source=:memory:",
            ["Storage:Provider"] = "Local",
            ["Storage:LocalRoot"] = root,
            ["Analytics:Sink"] = "Memory"
        });

        var services = new ServiceCollection();
        services.AddVhonaInfrastructure(config);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<LocalFileStorage>(provider.GetRequiredService<IFileStorage>());
    }

    [Fact]
    public void Entra_settings_must_all_be_present()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "EntraExternalId",
            ["Auth:EntraExternalId:Instance"] = "https://harbour.ciamlogin.com/",
            ["Auth:EntraExternalId:TenantId"] = "<TODO-entra-external-id-tenant-id>",
            ["Auth:EntraExternalId:ClientId"] = "11111111-1111-1111-1111-111111111111",
            ["Auth:EntraExternalId:ClientSecret"] = ""
        });

        var ex = Assert.Throws<InvalidOperationException>(() => HostingConfiguration.EnsureEntraConfigured(config));
        Assert.Contains("TenantId", ex.Message);
        Assert.Contains("ClientSecret", ex.Message);
    }

    [Fact]
    public void Default_organization_name_falls_back_to_the_demo_org()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["DemoAuth:OrganizationName"] = "Harbour Street Studio"
        });

        Assert.Equal("Harbour Street Studio", HostingConfiguration.DefaultOrganizationName(config));
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
