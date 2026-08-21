namespace GMS.Platform.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Platform.Entities;

public class PlatformInvoiceConfiguration : IEntityTypeConfiguration<PlatformInvoice>
{
    public void Configure(EntityTypeBuilder<PlatformInvoice> builder)
    {
        builder.ToTable("platform_invoices", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_platform_invoices_status",
                "[Status] IN ('issued','paid','overdue','voided')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.InvoiceNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Subtotal).HasPrecision(12, 2);
        builder.Property(x => x.VatAmount).HasPrecision(12, 2);
        builder.Property(x => x.Total).HasPrecision(12, 2);
        builder.Property(x => x.Currency).HasColumnType("char(3)").HasMaxLength(3).HasDefaultValue("EGP");
        builder.Property(x => x.Status).HasMaxLength(12).IsRequired();
        builder.Property(x => x.PaymentMethod).HasMaxLength(32);
        builder.Property(x => x.PaymentReference).HasMaxLength(128);
        builder.Property(x => x.PaymentLink).HasMaxLength(500);
        builder.Property(x => x.EtaUuid).HasMaxLength(128);
        builder.Property(x => x.PdfUrl).HasMaxLength(500);
        builder.Property(x => x.LinesSnapshot).HasColumnType("nvarchar(max)");

        builder.HasIndex(x => x.InvoiceNumber).IsUnique();
        builder.HasIndex(x => new { x.SubscriptionId, x.PeriodStart }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => x.DueDate);

        builder.HasOne<PlatformSubscription>()
            .WithMany()
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PlatformInvoiceSequenceConfiguration : IEntityTypeConfiguration<PlatformInvoiceSequence>
{
    public void Configure(EntityTypeBuilder<PlatformInvoiceSequence> builder)
    {
        builder.ToTable("platform_invoice_sequences", "platform");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Year).IsRequired();
        builder.Property(x => x.LastNumber).IsRequired();
        builder.HasIndex(x => x.Year).IsUnique();
    }
}

public class PlatformPaymentEventConfiguration : IEntityTypeConfiguration<PlatformPaymentEvent>
{
    public void Configure(EntityTypeBuilder<PlatformPaymentEvent> builder)
    {
        builder.ToTable("platform_payment_events", "platform");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Gateway).HasMaxLength(32).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ExternalRef).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(12, 2);
        builder.Property(x => x.RawPayload).HasColumnType("nvarchar(max)");

        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.InvoiceId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.SubscriptionId, x.CreatedAtUtc });
    }
}
