using VhonaAI.Core.Entities;
using VhonaAI.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace VhonaAI.Infrastructure.Data;

public sealed class VhonaDbContext : DbContext
{
    private readonly ICurrentUser? _currentUser;

    public VhonaDbContext(DbContextOptions<VhonaDbContext> options, ICurrentUser? currentUser = null)
        : base(options)
    {
        _currentUser = currentUser;
    }

    /// <summary>
    /// The signed-in business, or null when no business is in scope (startup, sign-in, tests).
    /// Null turns the tenant filter off. A set value hides every other business.
    /// </summary>
    public Guid? CurrentOrganizationId =>
        _currentUser is { IsAuthenticated: true, HasOrganization: true }
            ? _currentUser.OrganizationId
            : null;

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ColumnMappingProfile> ColumnMappingProfiles => Set<ColumnMappingProfile>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<WhyAnswer> WhyAnswers => Set<WhyAnswer>();
    public DbSet<WhyCitation> WhyCitations => Set<WhyCitation>();
    public DbSet<MorningBriefPreference> MorningBriefPreferences => Set<MorningBriefPreference>();
    public DbSet<MorningBrief> MorningBriefs => Set<MorningBrief>();
    public DbSet<MorningBriefCitation> MorningBriefCitations => Set<MorningBriefCitation>();
    public DbSet<DataSource> DataSources => Set<DataSource>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<CallThresholdSettings> CallThresholdSettings => Set<CallThresholdSettings>();
    public DbSet<AdminAuditEntry> AdminAuditEntries => Set<AdminAuditEntry>();
    public DbSet<PlatformCallDefaults> PlatformCallDefaults => Set<PlatformCallDefaults>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VhonaDbContext).Assembly);
        modelBuilder.Entity<Organization>().HasQueryFilter(organization =>
            CurrentOrganizationId == null || organization.Id == CurrentOrganizationId);
        ApplyTenantFilter<Membership>(modelBuilder);
        ApplyTenantFilter<ImportJob>(modelBuilder);
        ApplyTenantFilter<ColumnMappingProfile>(modelBuilder);
        ApplyTenantFilter<Transaction>(modelBuilder);
        ApplyTenantFilter<WhyAnswer>(modelBuilder);
        ApplyTenantFilter<MorningBriefPreference>(modelBuilder);
        ApplyTenantFilter<MorningBrief>(modelBuilder);
        ApplyTenantFilter<DataSource>(modelBuilder);
        ApplyTenantFilter<Customer>(modelBuilder);
        ApplyTenantFilter<Invoice>(modelBuilder);
        ApplyTenantFilter<InvoiceLine>(modelBuilder);
        ApplyTenantFilter<Payment>(modelBuilder);
        ApplyTenantFilter<CreditNote>(modelBuilder);
        ApplyTenantFilter<Invitation>(modelBuilder);
        ApplyTenantFilter<CallThresholdSettings>(modelBuilder);
        modelBuilder.Entity<WhyCitation>().HasQueryFilter(citation =>
            CurrentOrganizationId == null || citation.WhyAnswer.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<MorningBriefCitation>().HasQueryFilter(citation =>
            CurrentOrganizationId == null || citation.MorningBrief.OrganizationId == CurrentOrganizationId);
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IOrganizationOwned
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity =>
            CurrentOrganizationId == null || entity.OrganizationId == CurrentOrganizationId);
    }

    private void EnforceTenant()
    {
        var actor = AdminDataAccess.ActorUserId;
        if (actor is not null)
        {
            if (_currentUser is not { IsAuthenticated: true } || _currentUser.UserId != actor)
            {
                throw new InvalidOperationException("Admin data access does not match the signed-in person.");
            }

            return;
        }

        var tenant = CurrentOrganizationId;
        if (tenant is null)
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is Organization organization && organization.Id != tenant)
            {
                throw new InvalidOperationException("This change belongs to a different business.");
            }

            if (entry.Entity is IOrganizationOwned owned && owned.OrganizationId != tenant)
            {
                throw new InvalidOperationException("This change belongs to a different business.");
            }
        }
    }
}

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DisabledAt);
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
        builder.Property(x => x.DisabledAt);
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
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).HasDefaultValue(ImportKind.Transactions);
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
        builder.HasOne(x => x.DataSource)
            .WithMany()
            .HasForeignKey(x => x.DataSourceId)
            .OnDelete(DeleteBehavior.Restrict);
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

internal sealed class MorningBriefPreferenceConfiguration : IEntityTypeConfiguration<MorningBriefPreference>
{
    public void Configure(EntityTypeBuilder<MorningBriefPreference> builder)
    {
        builder.ToTable("MorningBriefPreferences");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
        builder.HasOne(x => x.Organization)
            .WithMany(x => x.MorningBriefPreferences)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MorningBriefConfiguration : IEntityTypeConfiguration<MorningBrief>
{
    public void Configure(EntityTypeBuilder<MorningBrief> builder)
    {
        builder.ToTable("MorningBriefs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SnapshotJson).IsRequired();
        builder.Property(x => x.CurrentPeriodLabel).HasMaxLength(32);
        builder.Property(x => x.PreviousPeriodLabel).HasMaxLength(32);
        builder.Property(x => x.Explanation).IsRequired();
        builder.Property(x => x.Metric).HasMaxLength(32);
        builder.HasIndex(x => new { x.OrganizationId, x.UserId, x.BriefDate }).IsUnique();
        builder.HasOne(x => x.Organization)
            .WithMany(x => x.MorningBriefs)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.WhyAnswer)
            .WithMany()
            .HasForeignKey(x => x.WhyAnswerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class MorningBriefCitationConfiguration : IEntityTypeConfiguration<MorningBriefCitation>
{
    public void Configure(EntityTypeBuilder<MorningBriefCitation> builder)
    {
        builder.ToTable("MorningBriefCitations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Columns).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PeriodLabel).HasMaxLength(32);
        builder.HasIndex(x => new { x.MorningBriefId, x.RowId }).IsUnique();
        builder.HasOne(x => x.MorningBrief)
            .WithMany(x => x.Citations)
            .HasForeignKey(x => x.MorningBriefId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
