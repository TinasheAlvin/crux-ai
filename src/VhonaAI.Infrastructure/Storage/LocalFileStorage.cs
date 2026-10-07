using VhonaAI.Core.Storage;

namespace VhonaAI.Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(
        Stream content,
        Guid organizationId,
        Guid importJobId,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        var safeName = Path.GetFileName(originalFileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "upload.csv";
        }

        var relative = Path.Combine(organizationId.ToString("N"), importJobId.ToString("N"), safeName)
            .Replace('\\', '/');
        var fullPath = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, cancellationToken);
        return relative;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(storagePath);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return Task.CompletedTask;
        }

        var fullPath = Resolve(storagePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string Resolve(string storagePath)
    {
        var combined = Path.GetFullPath(Path.Combine(_root, storagePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(_root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid storage path.");
        }

        return combined;
    }
}
