using CruxAI.Core.Storage;

namespace CruxAI.Infrastructure.Storage;

/// <summary>
/// Placeholder for the Azure Blob implementation.
/// TODO: add Azure.Storage.Blobs, read Storage:AzureBlob:ConnectionString from user-secrets,
/// and upload to Storage:AzureBlob:ContainerName.
/// </summary>
public sealed class AzureBlobFileStorage : IFileStorage
{
    public Task<string> SaveAsync(
        Stream content,
        Guid organizationId,
        Guid importJobId,
        string originalFileName,
        CancellationToken cancellationToken = default) =>
        throw NotWired();

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default) =>
        throw NotWired();

    private static InvalidOperationException NotWired() =>
        new("Azure Blob storage is not implemented yet. Set Storage:Provider to Local for the local demo, or implement AzureBlobFileStorage with Azure.Storage.Blobs.");
}
