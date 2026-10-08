namespace VhonaAI.Core.Admin;

/// <summary>
/// Admin pages are statically prerendered and then attached to the interactive circuit.
/// Both passes run OnInitialized and OnParametersSet, so a page view is recorded only
/// after <see cref="MarkInteractive"/>, and only once per key.
/// </summary>
public sealed class AdminViewGuard
{
    private readonly HashSet<string> _recorded = new(StringComparer.Ordinal);

    public bool IsInteractive { get; private set; }

    public void MarkInteractive() => IsInteractive = true;

    public bool TryRecord(string key)
    {
        if (!IsInteractive || string.IsNullOrEmpty(key))
        {
            return false;
        }

        return _recorded.Add(key);
    }
}
