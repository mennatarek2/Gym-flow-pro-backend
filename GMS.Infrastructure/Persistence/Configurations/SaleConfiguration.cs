namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.MemberId);
        builder.Property(s => s.GuestName).HasMaxLength(200).HasColumnType("NVARCHAR(200)");
        builder.Property(s => s.GuestPhone).HasMaxLength(30).HasColumnType("NVARCHAR(30)");
        builder.Property(s => s.SoldByUserId).IsRequired();

        builder.Property(s => s.ShiftId);

        builder.Property(s => s.Subtotal).HasColumnType("DECIMAL(12,2)").IsRequired();
        builder.Property(s => s.DiscountAmount).HasColumnType("DECIMAL(12,2)").IsRequired();
        builder.Property(s => s.TaxAmount).HasColumnType("DECIMAL(12,2)").IsRequired();
        builder.Property(s => s.Total).HasColumnType("DECIMAL(12,2)").IsRequired();

        builder.Property(s => s.PromoCodeId);
        builder.Property(s => s.ManualDiscountAmount).HasColumnType("DECIMAL(12,2)");
        builder.Property(s => s.ManualDiscountReason).HasMaxLength(200).HasColumnType("NVARCHAR(200)");

        builder.Property(s => s.AmountDue).HasColumnType("DECIMAL(12,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(s => s.DueDate).HasColumnType("DATE");

        // VARCHAR(20): "partially_refunded" is 19 chars — VARCHAR(15) (sized only for "partially_paid",
        // 14 chars) silently truncated it and caused a SQL data-truncation error on save.
        builder.Property(s => s.Status).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)").HasDefaultValue("completed");
        builder.Property(s => s.IdempotencyKey).HasMaxLength(100).HasColumnType("NVARCHAR(100)");

        builder.Property(s => s.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(s => s.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(s => s.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(s => new { s.TenantId, s.Status });
        builder.HasIndex(s => new { s.TenantId, s.MemberId });
        builder.HasIndex(s => new { s.TenantId, s.IdempotencyKey })
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL");

        builder.HasOne(s => s.Tenant).WithMany()
            .HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Member).WithMany()
            .HasForeignKey(s => s.MemberId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.SoldByUser).WithMany()
            .HasForeignKey(s => s.SoldByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.PromoCode).WithMany()
            .HasForeignKey(s => s.PromoCodeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Shift).WithMany()
            .HasForeignKey(s => s.ShiftId).OnDelete(DeleteBehavior.SetNull);

        builder.ToTable("sales");
    }
}
