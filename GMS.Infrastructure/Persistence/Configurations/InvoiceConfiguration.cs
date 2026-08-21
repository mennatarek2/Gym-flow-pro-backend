namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(i => i.TenantId).IsRequired();
        builder.Property(i => i.Type).IsRequired().HasMaxLength(12).HasColumnType("VARCHAR(12)");
        builder.Property(i => i.InvoiceNumber).IsRequired().HasMaxLength(20).HasColumnType("NVARCHAR(20)");

        builder.Property(i => i.RefundId);

        builder.Property(i => i.MemberNameSnapshot).IsRequired().HasMaxLength(200).HasColumnType("NVARCHAR(200)");
        builder.Property(i => i.MemberPhoneSnapshot).IsRequired().HasMaxLength(20).HasColumnType("NVARCHAR(20)");
        builder.Property(i => i.LinesSnapshot).IsRequired().HasColumnType("NVARCHAR(MAX)");

        builder.Property(i => i.Subtotal).HasColumnType("DECIMAL(12,2)").IsRequired();
        builder.Property(i => i.DiscountAmount).HasColumnType("DECIMAL(12,2)").IsRequired();
        builder.Property(i => i.VatRate).HasColumnType("DECIMAL(5,2)").IsRequired();
        builder.Property(i => i.VatAmount).HasColumnType("DECIMAL(12,2)").IsRequired();
        builder.Property(i => i.Total).HasColumnType("DECIMAL(12,2)").IsRequired();
        builder.Property(i => i.Currency).IsRequired().HasMaxLength(3).HasColumnType("CHAR(3)").HasDefaultValue("EGP");

        builder.Property(i => i.IssuedAt).HasColumnType("DATETIME2").IsRequired();
        builder.Property(i => i.PdfUrl).HasMaxLength(500).HasColumnType("NVARCHAR(500)");

        builder.Property(i => i.Status).IsRequired().HasMaxLength(12).HasColumnType("VARCHAR(12)").HasDefaultValue("issued");
        builder.Property(i => i.VoidReason).HasMaxLength(200).HasColumnType("NVARCHAR(200)");

        builder.Property(i => i.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(i => i.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(i => i.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(i => new { i.TenantId, i.InvoiceNumber }).IsUnique();
        builder.HasIndex(i => new { i.TenantId, i.SaleId });
        builder.HasIndex(i => new { i.TenantId, i.Status });

        builder.HasOne(i => i.Tenant).WithMany()
            .HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Sale).WithMany()
            .HasForeignKey(i => i.SaleId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Refund).WithMany()
            .HasForeignKey(i => i.RefundId).OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(i => i.OriginalInvoice).WithMany()
            .HasForeignKey(i => i.OriginalInvoiceId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.VoidedByUser).WithMany()
            .HasForeignKey(i => i.VoidedByUserId).OnDelete(DeleteBehavior.SetNull);

        builder.ToTable("invoices", tb =>
            tb.HasCheckConstraint("CK_invoices_LinesSnapshot_IsJson", "ISJSON([LinesSnapshot]) = 1"));
    }
}
