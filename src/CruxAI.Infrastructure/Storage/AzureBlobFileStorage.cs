using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CruxAI.Core.Storage;
using CruxAI.Infrastructure.Hosting;

namespace CruxAI.Infrastructure.Storage;

/// <summary>
/// Persists CSV uploads in Azure Blob Storage. The import job stores the blob name,
/// and later reads (map, validate, refresh) open that blob again.
/// </summary>
public sealed class AzureBlobFileStorage : IFileStorage
{
    private readonly BlobContainerClient _container;
    private readonly SemaphoreSlim _ready = new(1, 1);
    private bool _containerReady;

    public AzureBlobFileStorage(string connectionString, string containerName)
    {
        connectionString = HostingConfiguration.RequireResolvedSecret(
            connectionString,
            HostingConfiguration.AzureBlobMissingMessage);

        if (string.IsNullOrWhiteSpace(containerName) || HostingConfiguration.IsUnset(containerName))
        {
            containerName = "csv-uploads";
        }

        if (containerName.Contains('/') || containerName.Contains('\\'))
        {
            throw new InvalidOperationException("Storage:AzureBlob:ContainerName must be a single container name.");
        }

        _container = new BlobContainerClient(connectionString, containerName);
    }

    public static AzureBlobFileStorage FromConfiguration(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var container = configuration["Storage:AzureBlob:ContainerName"];
        return new AzureBlobFileStorage(
            configuration["Storage:AzureBlob:ConnectionString"] ?? string.Empty,
            string.IsNullOrWhiteSpace(container) ? "csv-uploads" : container);
    }

    public async Task<string> SaveAsync(
        Stream content,
        Guid organizationId,
        Guid importJobId,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        var blobName = BlobUploadPath.Build(organizationId, importJobId, originalFileName);
        await EnsureContainerAsync(cancellationToken);
        var blob = _container.GetBlobClient(blobName);
        await blob.UploadAsync(content, overwrite: true, cancellationToken);
        return blobName;
    }

    public async Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        BlobUploadPath.EnsureSafe(storagePath);
        await EnsureContainerAsync(cancellationToken);
        var blob = _container.GetBlobClient(storagePath);
        try
        {
            var response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return response.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException("Stored CSV was not found in Azure Blob.", ex);
        }
    }

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_containerReady)
        {
            return;
        }

        await _ready.WaitAsync(cancellationToken);
        try
        {
            if (_containerReady)
            {
                return;
            }

            await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
            _containerReady = true;
        }
        finally
        {
            _ready.Release();
        }
    }
}
