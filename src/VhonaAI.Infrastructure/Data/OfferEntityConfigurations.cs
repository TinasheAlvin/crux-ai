using VhonaAI.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace VhonaAI.Infrastructure.Data;

internal sealed class DataSourceConfiguration : IEntityTypeConfiguration<DataSource>
{
    public void Configure(EntityTypeBuilder<DataSource> builder)
    {
        builder.ToTable("DataSources");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(260).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ExternalSystem).HasMaxLength(32);
        builder.HasIndex(x => x.ImportJobId);
        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ImportJob)
            .WithMany()
            .HasForeignKey(x => x.ImportJobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ContactPerson).HasMaxLength(200);
        builder.Property(x => x.Phone).HasMaxLength(40);
        builder.Property(x => x.Email).HasMaxLength(320);
        builder.Property(x => x.SourceExternalId).HasMaxLength(128);
        builder.HasIndex(x => new { x.OrganizationId, x.RowId }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.NormalizedName }).IsUnique();
        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.DataSource)
            .WithMany()
            .HasForeignKey(x => x.DataSourceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Number).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.AmountDue).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Terms).HasMaxLength(100);
        builder.HasIndex(x => new { x.OrganizationId, x.RowId }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.Number });
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.ImportJobId);
        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Customer)
            .WithMany(x => x.Invoices)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DataSource)
            .WithMany()
            .HasForeignKey(x => x.DataSourceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ImportJob)
            .WithMany()
            .HasForeignKey(x => x.ImportJobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("InvoiceLines");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.ItemCode).HasMaxLength(100);
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,4)");
        builder.HasIndex(x => new { x.OrganizationId, x.RowId }).IsUnique();
        builder.HasIndex(x => x.InvoiceId);
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Invoice)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Reference).HasMaxLength(100);
        builder.HasIndex(x => new { x.OrganizationId, x.RowId }).IsUnique();
        builder.HasIndex(x => x.InvoiceId);
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Invoice)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DataSource)
            .WithMany()
            .HasForeignKey(x => x.DataSourceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ImportJob)
            .WithMany()
            .HasForeignKey(x => x.ImportJobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("CreditNotes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Number).HasMaxLength(100);
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.HasIndex(x => new { x.OrganizationId, x.RowId }).IsUnique();
        builder.HasIndex(x => x.InvoiceId);
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Invoice)
            .WithMany(x => x.CreditNotes)
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DataSource)
            .WithMany()
            .HasForeignKey(x => x.DataSourceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ImportJob)
            .WithMany()
            .HasForeignKey(x => x.ImportJobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("Invitations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Email).HasMaxLength(320).IsRequired();
        builder.Property(x => x.Token).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(x => x.Token).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.Email });
        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CallThresholdSettingsConfiguration : IEntityTypeConfiguration<CallThresholdSettings>
{
    public void Configure(EntityTypeBuilder<CallThresholdSettings> builder)
    {
        builder.ToTable("CallThresholdSettings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DroppedPercent).HasColumnType("decimal(5,2)");
        builder.HasIndex(x => x.OrganizationId).IsUnique();
        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AdminAuditEntryConfiguration : IEntityTypeConfiguration<AdminAuditEntry>
{
    public void Configure(EntityTypeBuilder<AdminAuditEntry> builder)
    {
        builder.ToTable("AdminAuditEntries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActorEmail).HasMaxLength(320).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Target).HasMaxLength(64);
        builder.Property(x => x.Summary).HasMaxLength(2000).IsRequired();
        builder.HasIndex(x => x.At);
    }
}

internal sealed class PlatformCallDefaultsConfiguration : IEntityTypeConfiguration<PlatformCallDefaults>
{
    public void Configure(EntityTypeBuilder<PlatformCallDefaults> builder)
    {
        builder.ToTable("PlatformCallDefaults");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DroppedPercent).HasColumnType("decimal(5,2)");
    }
}

internal sealed class CallCustomerActionConfiguration : IEntityTypeConfiguration<CallCustomerAction>
{
    public void Configure(EntityTypeBuilder<CallCustomerAction> builder)
    {
        builder.ToTable("CallCustomerActions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.EvidenceKey).HasMaxLength(2000);
        builder.HasIndex(x => new { x.OrganizationId, x.CustomerId, x.At });
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReminderDraftConfiguration : IEntityTypeConfiguration<ReminderDraft>
{
    public void Configure(EntityTypeBuilder<ReminderDraft> builder)
    {
        builder.ToTable("ReminderDrafts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.CustomerId }).IsUnique();
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
