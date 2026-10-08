using System.Text.RegularExpressions;

namespace VhonaAI.Core.Parsing;

public static class CustomerNames
{
    public static string Normalize(string name)
    {
        var collapsed = Regex.Replace(name.Trim(), @"\s+", " ");
        return collapsed.ToUpperInvariant();
    }
}
