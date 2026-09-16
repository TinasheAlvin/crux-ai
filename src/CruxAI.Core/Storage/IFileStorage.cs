namespace CruxAI.Core.Storage;

public interface IFileStorage
{
    Task<string> SaveAsync(
        Stream content,
        Guid organizationId,
        Guid importJobId,
        string originalFileName,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);
}
