namespace VhonaAI.Core.Entities;

public sealed class WhyCitation
{
    public Guid Id { get; set; }
    public Guid WhyAnswerId { get; set; }

    public string RowId { get; set; } = string.Empty;
    public string Columns { get; set; } = string.Empty;
    public string? PeriodLabel { get; set; }

    public WhyAnswer WhyAnswer { get; set; } = null!;
}
