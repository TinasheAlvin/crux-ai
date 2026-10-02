namespace CruxAI.Infrastructure.Storage;

/// <summary>
/// Blob names for CSV uploads. The same relative path is stored on the import job
/// and opened again after a refresh, so it must not depend on the local disk.
/// </summary>
public static class BlobUploadPath
{
    public static string Build(Guid organizationId, Guid importJobId, string originalFileName)
    {
        var safeName = SanitizeFileName(originalFileName);
        return $"{organizationId:N}/{importJobId:N}/{safeName}";
    }

    public static string SanitizeFileName(string originalFileName)
    {
        var name = Path.GetFileName((originalFileName ?? string.Empty).Replace('\\', '/').Trim());
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            return "upload.csv";
        }

        var cleaned = new string(name.Where(c => !char.IsControl(c) && c is not '?' and not '#').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "upload.csv" : cleaned;
    }

    public static void EnsureSafe(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath)
            || storagePath.Contains('\\', StringComparison.Ordinal)
            || storagePath.Contains("..", StringComparison.Ordinal)
            || storagePath.StartsWith('/')
            || storagePath.Contains('?', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid storage path.");
        }
    }
}
