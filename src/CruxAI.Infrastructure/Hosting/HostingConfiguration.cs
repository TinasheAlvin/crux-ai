using Microsoft.Extensions.Configuration;

namespace CruxAI.Infrastructure.Hosting;

/// <summary>
/// Reads the provider switches shared by local demo and the Azure host.
/// Local defaults stay Demo + Sqlite + Local. Azure providers fail at startup
/// when their settings are still placeholders.
/// </summary>
public static class HostingConfiguration
{
    public const string SqliteProvider = "Sqlite";
    public const string AzureSqlProvider = "AzureSql";
    public const string LocalStorageProvider = "Local";
    public const string AzureBlobProvider = "AzureBlob";
    public const string DemoAuthProvider = "Demo";
    public const string EntraAuthProvider = "EntraExternalId";

    public const string AzureSqlMissingMessage =
        "Database:Provider is AzureSql but ConnectionStrings:AzureSql is missing or still a placeholder. " +
        "Set it with user-secrets, an App Service setting, or a resolved Key Vault reference. Do not commit the secret.";

    public const string AzureBlobMissingMessage =
        "Storage:Provider is AzureBlob but Storage:AzureBlob:ConnectionString is missing or still a placeholder. " +
        "Set it with user-secrets, an App Service setting, or a resolved Key Vault reference. Do not commit the secret.";

    public static bool IsAzureSql(IConfiguration configuration) =>
        string.Equals(configuration["Database:Provider"], AzureSqlProvider, StringComparison.OrdinalIgnoreCase);

    public static bool IsAzureBlob(IConfiguration configuration) =>
        string.Equals(configuration["Storage:Provider"], AzureBlobProvider, StringComparison.OrdinalIgnoreCase);

    public static bool IsEntra(IConfiguration configuration) =>
        string.Equals(configuration["Auth:Provider"], EntraAuthProvider, StringComparison.OrdinalIgnoreCase);

    public static string DefaultOrganizationName(IConfiguration configuration)
    {
        var configured = configuration["Auth:DefaultOrganizationName"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        var demo = configuration["DemoAuth:OrganizationName"];
        if (!string.IsNullOrWhiteSpace(demo))
        {
            return demo.Trim();
        }

        return "Harbour Street Studio";
    }

    public static bool IsUnresolvedKeyVaultReference(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.TrimStart().StartsWith("@Microsoft.KeyVault(", StringComparison.OrdinalIgnoreCase);

    public static bool IsUnset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains('<') && trimmed.Contains('>'))
        {
            return true;
        }

        if (trimmed.Contains("YOUR_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (trimmed.Contains("AccountKey=;", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("Password=;", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("User ID=;", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (trimmed.Equals("TODO", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("TODO-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static string RequireResolvedSecret(string? value, string message)
    {
        if (IsUnresolvedKeyVaultReference(value))
        {
            throw new InvalidOperationException(
                "A Key Vault reference was not resolved before the app started. " +
                "Grant the App Service identity secret get permission, then restart. " + message);
        }

        if (IsUnset(value))
        {
            throw new InvalidOperationException(message);
        }

        return value!.Trim();
    }

    public static string RequireAzureSqlConnectionString(IConfiguration configuration) =>
        RequireResolvedSecret(configuration.GetConnectionString("AzureSql"), AzureSqlMissingMessage);

    public static string RequireAzureBlobConnectionString(IConfiguration configuration) =>
        RequireResolvedSecret(configuration["Storage:AzureBlob:ConnectionString"], AzureBlobMissingMessage);

    public static void EnsureEntraConfigured(IConfiguration configuration)
    {
        var section = configuration.GetSection("Auth:EntraExternalId");
        string[] keys = ["Instance", "TenantId", "ClientId", "ClientSecret"];
        var missing = new List<string>();
        foreach (var key in keys)
        {
            var value = section[key];
            if (IsUnresolvedKeyVaultReference(value) || IsUnset(value))
            {
                missing.Add("Auth:EntraExternalId:" + key);
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Auth:Provider is EntraExternalId but these settings are missing, placeholders, or unresolved Key Vault references: "
                + string.Join(", ", missing)
                + ". Set them with user-secrets or App Service settings. Do not commit secrets.");
        }
    }
}
