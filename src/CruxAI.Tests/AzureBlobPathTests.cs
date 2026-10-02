using CruxAI.Infrastructure.Storage;

namespace CruxAI.Tests;

public class AzureBlobPathTests
{
    [Fact]
    public void Build_uses_org_and_import_ids_and_strips_directories()
    {
        var orgId = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var importId = Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        var name = BlobUploadPath.Build(orgId, importId, "..\\..\\secret.csv");
        Assert.Equal($"{orgId:N}/{importId:N}/secret.csv", name);
        BlobUploadPath.EnsureSafe(name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("org\\job\\file.csv")]
    public void Unsafe_paths_are_rejected(string path)
    {
        Assert.Throws<InvalidOperationException>(() => BlobUploadPath.EnsureSafe(path));
    }

    [Fact]
    public void Empty_file_name_becomes_upload_csv()
    {
        var name = BlobUploadPath.Build(Guid.NewGuid(), Guid.NewGuid(), "   ");
        Assert.EndsWith("/upload.csv", name);
    }
}
