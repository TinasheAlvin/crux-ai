namespace CruxAI.Infrastructure.Why;

public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    public string Endpoint { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "2024-10-21";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(DeploymentName)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !Endpoint.Contains('<', StringComparison.Ordinal)
        && !Endpoint.Contains("TODO", StringComparison.OrdinalIgnoreCase)
        && !ApiKey.Contains("TODO", StringComparison.OrdinalIgnoreCase)
        && !ApiKey.Contains("YOUR_", StringComparison.OrdinalIgnoreCase)
        && !DeploymentName.Contains('<', StringComparison.Ordinal);
}
