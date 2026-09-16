namespace CruxAI.Core.Analytics;

/// <summary>
/// Distinguishes Blazor prerender dispose (must not count as abandon) from leaving the interactive map page.
/// </summary>
public sealed class MapAbandonGuard
{
    public bool MappingSaved { get; private set; }
    public bool Interactive { get; private set; }

    public void MarkInteractive() => Interactive = true;

    public void MarkSaved() => MappingSaved = true;

    public bool ShouldTrackAbandon => Interactive && !MappingSaved;
}
