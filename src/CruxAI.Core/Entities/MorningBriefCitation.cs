namespace CruxAI.Core.Entities;

public sealed class MorningBriefCitation
{
    public Guid Id { get; set; }
    public Guid MorningBriefId { get; set; }

    public string RowId { get; set; } = string.Empty;
    public string Columns { get; set; } = string.Empty;
    public string? PeriodLabel { get; set; }

    public MorningBrief MorningBrief { get; set; } = null!;
}
