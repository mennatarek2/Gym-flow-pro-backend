namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.SaleId).IsRequired();
        builder.Property(r => r.PaymentTransactionId);

        builder.Property(r => r.Amount).HasColumnType("DECIMAL(12,2)").IsRequired();
        builder.Property(r => r.Method).IsRequired().HasMaxLength(15).HasColumnType("VARCHAR(15)");
        builder.Property(r => r.Reason).IsRequired().HasMaxLength(300).HasColumnType("NVARCHAR(300)");

        builder.Property(r => r.RequestedByUserId).IsRequired();
        builder.Property(r => r.ApprovedByUserId);

        builder.Property(r => r.Status)
            .IsRequired().HasMaxLength(12).HasColumnType("VARCHAR(12)").HasDefaultValue("requested");

        builder.Property(r => r.RejectionNote).HasMaxLength(300).HasColumnType("NVARCHAR(300)");
        builder.Property(r => r.CreditNoteInvoiceId);
        builder.Property(r => r.ExecutedAt).HasColumnType("DATETIME2");

        builder.Property(r => r.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(r => r.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(r => r.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(r => new { r.TenantId, r.SaleId });
        builder.HasIndex(r => new { r.TenantId, r.Status });

        builder.HasOne(r => r.Tenant).WithMany()
            .HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Sale).WithMany()
            .HasForeignKey(r => r.SaleId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.PaymentTransaction).WithMany()
            .HasForeignKey(r => r.PaymentTransactionId).OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.RequestedByUser).WithMany()
            .HasForeignKey(r => r.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ApprovedByUser).WithMany()
            .HasForeignKey(r => r.ApprovedByUserId).OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.CreditNoteInvoice).WithMany()
            .HasForeignKey(r => r.CreditNoteInvoiceId).OnDelete(DeleteBehavior.SetNull);

        builder.ToTable("refunds", tb =>
        {
            tb.HasCheckConstraint("CK_refunds_Method", "Method IN ('cash','gateway','credit')");
            tb.HasCheckConstraint("CK_refunds_Status", "Status IN ('requested','approved','executed','rejected')");
        });
    }
}
