using CruxAI.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CruxAI.Infrastructure.Data;

public sealed class CruxDbContext : DbContext
{
    public CruxDbContext(DbContextOptions<CruxDbContext> options) : base(options)
    {
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ColumnMappingProfile> ColumnMappingProfiles => Set<ColumnMappingProfile>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<WhyAnswer> WhyAnswers => Set<WhyAnswer>();
    public DbSet<WhyCitation> WhyCitations => Set<WhyCitation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CruxDbContext).Assembly);
    }
}

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
    }
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExternalId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(320).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.Email).IsUnique();
        builder.HasIndex(x => x.ExternalId).IsUnique();
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("Memberships");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
        builder.HasOne(x => x.Organization)
            .WithMany(x => x.Memberships)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User)
            .WithMany(x => x.Memberships)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> builder)
    {
        builder.ToTable("ImportJobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.StoragePath).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.MappingJson);
        builder.Property(x => x.CorrectionsJson);
        builder.Property(x => x.ErrorSummary).HasMaxLength(2000);
        builder.HasOne(x => x.Organization)
            .WithMany(x => x.ImportJobs)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ColumnMappingProfileConfiguration : IEntityTypeConfiguration<ColumnMappingProfile>
{
    public void Configure(EntityTypeBuilder<ColumnMappingProfile> builder)
    {
        builder.ToTable("ColumnMappingProfiles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.MappingJson).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.Name }).IsUnique();
        builder.HasOne(x => x.Organization)
            .WithMany(x => x.MappingProfiles)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Balance).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(100);
        builder.Property(x => x.Reference).HasMaxLength(100);
        builder.Property(x => x.Counterparty).HasMaxLength(200);
        builder.HasIndex(x => new { x.OrganizationId, x.RowId }).IsUnique();
        builder.HasIndex(x => x.ImportJobId);
        builder.HasOne(x => x.Organization)
            .WithMany(x => x.Transactions)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ImportJob)
            .WithMany(x => x.Transactions)
            .HasForeignKey(x => x.ImportJobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WhyAnswerConfiguration : IEntityTypeConfiguration<WhyAnswer>
{
    public void Configure(EntityTypeBuilder<WhyAnswer> builder)
    {
        builder.ToTable("WhyAnswers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Question).IsRequired();
        builder.Property(x => x.Answer).IsRequired();
        builder.Property(x => x.Metric).HasMaxLength(32);
        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasOne(x => x.Organization)
            .WithMany(x => x.WhyAnswers)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WhyCitationConfiguration : IEntityTypeConfiguration<WhyCitation>
{
    public void Configure(EntityTypeBuilder<WhyCitation> builder)
    {
        builder.ToTable("WhyCitations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Columns).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PeriodLabel).HasMaxLength(32);
        builder.HasIndex(x => new { x.WhyAnswerId, x.RowId }).IsUnique();
        builder.HasOne(x => x.WhyAnswer)
            .WithMany(x => x.Citations)
            .HasForeignKey(x => x.WhyAnswerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
