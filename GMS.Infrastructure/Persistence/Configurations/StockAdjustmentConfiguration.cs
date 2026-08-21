namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> builder)
    {
        builder.ToTable("stock_adjustments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.WarehouseId).IsRequired();
        builder.Property(a => a.Status).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(a => a.ReasonCode).IsRequired().HasMaxLength(32).HasColumnType("VARCHAR(32)");
        builder.Property(a => a.Note).HasMaxLength(500).HasColumnType("NVARCHAR(500)");
        builder.Property(a => a.CreatedByUserId).IsRequired();
        builder.Property(a => a.PostedByUserId);
        builder.Property(a => a.PostedAtUtc);

        builder.Property(a => a.CreatedAtUtc).IsRequired();
        builder.Property(a => a.UpdatedAtUtc);
        builder.Property(a => a.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(a => new { a.TenantId, a.Status });
        builder.HasIndex(a => new { a.TenantId, a.WarehouseId });

        builder.HasOne(a => a.Tenant).WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Warehouse).WithMany().HasForeignKey(a => a.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.CreatedByUser).WithMany().HasForeignKey(a => a.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.PostedByUser).WithMany().HasForeignKey(a => a.PostedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(a => a.Lines)
            .WithOne(l => l.StockAdjustment)
            .HasForeignKey(l => l.StockAdjustmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class StockAdjustmentLineConfiguration : IEntityTypeConfiguration<StockAdjustmentLine>
{
    public void Configure(EntityTypeBuilder<StockAdjustmentLine> builder)
    {
        builder.ToTable("stock_adjustment_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.StockAdjustmentId).IsRequired();
        builder.Property(l => l.ProductId).IsRequired();
        builder.Property(l => l.QtyDelta).IsRequired().HasColumnType("DECIMAL(18,3)");
        builder.Property(l => l.UnitCost).HasColumnType("DECIMAL(14,2)");
        builder.Property(l => l.BatchId);

        builder.Property(l => l.CreatedAtUtc).IsRequired();
        builder.Property(l => l.UpdatedAtUtc);
        builder.Property(l => l.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(l => new { l.TenantId, l.StockAdjustmentId });
        builder.HasIndex(l => new { l.TenantId, l.BatchId });

        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Batch).WithMany().HasForeignKey(l => l.BatchId).OnDelete(DeleteBehavior.Restrict);
    }
}
