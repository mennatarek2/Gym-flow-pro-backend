namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public sealed class SaleAdjustmentConfiguration : IEntityTypeConfiguration<SaleAdjustment>
{
    public void Configure(EntityTypeBuilder<SaleAdjustment> builder)
    {
        builder.ToTable("sale_adjustments");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();
        builder.Property(item => item.TenantId).IsRequired();
        builder.Property(item => item.SaleId).IsRequired();
        builder.Property(item => item.Amount).HasColumnType("DECIMAL(14,2)").IsRequired();
        builder.Property(item => item.Type).HasMaxLength(30).HasColumnType("VARCHAR(30)").IsRequired();
        builder.Property(item => item.Status).HasMaxLength(20).HasColumnType("VARCHAR(20)").IsRequired();
        builder.Property(item => item.Reason).HasMaxLength(500).HasColumnType("NVARCHAR(500)").IsRequired();
        builder.Property(item => item.CreatedByUserId).IsRequired();
        builder.Property(item => item.IsDeleted).HasDefaultValue(false);
        builder.HasIndex(item => new { item.TenantId, item.SaleId, item.Status });
        builder.HasOne(item => item.Tenant).WithMany().HasForeignKey(item => item.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.Sale).WithMany().HasForeignKey(item => item.SaleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.CreatedByUser).WithMany().HasForeignKey(item => item.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
