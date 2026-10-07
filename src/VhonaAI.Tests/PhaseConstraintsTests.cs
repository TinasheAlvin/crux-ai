using VhonaAI.Application.Imports;

namespace VhonaAI.Tests;

public class PhaseConstraintsTests
{
    [Fact]
    public void Application_services_do_not_depend_on_infrastructure_or_web()
    {
        var names = typeof(IImportAppService).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToList();
        Assert.Contains("VhonaAI.Core", names);
        Assert.DoesNotContain("VhonaAI.Infrastructure", names);
        Assert.DoesNotContain("VhonaAI.Web", names);
    }

    [Fact]
    public void Source_has_no_customer_message_sender()
    {
        var root = RepoRoot();
        var forbidden = new[]
        {
            "SmtpClient",
            "MailKit",
            "SendGrid",
            "Twilio",
            "api.whatsapp.com",
            "graph.facebook.com"
        };

        var hits = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}VhonaAI.Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .SelectMany(file => forbidden.Where(token => file.Text.Contains(token, StringComparison.Ordinal)).Select(token => $"{token} in {file.Path}"))
            .ToList();

        Assert.Empty(hits);
    }

    [Fact]
    public void Azure_region_stays_south_africa_north()
    {
        var bicep = File.ReadAllText(Path.Combine(RepoRoot(), "infra", "main.bicep"));
        Assert.Contains("param location string = 'southafricanorth'", bicep);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "VhonaAI.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
