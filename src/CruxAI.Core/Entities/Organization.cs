namespace CruxAI.Core.Entities;

public sealed class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
    public ICollection<ImportJob> ImportJobs { get; set; } = new List<ImportJob>();
    public ICollection<ColumnMappingProfile> MappingProfiles { get; set; } = new List<ColumnMappingProfile>();
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<WhyAnswer> WhyAnswers { get; set; } = new List<WhyAnswer>();
}
