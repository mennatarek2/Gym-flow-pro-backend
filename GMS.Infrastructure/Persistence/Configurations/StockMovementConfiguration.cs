namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(m => m.TenantId).IsRequired();
        builder.Property(m => m.ProductId).IsRequired();
        builder.Property(m => m.WarehouseId).IsRequired();
        builder.Property(m => m.BatchId);

        builder.Property(m => m.QtyDelta).IsRequired().HasColumnType("DECIMAL(18,3)");
        builder.Property(m => m.UnitCost).HasColumnType("DECIMAL(14,2)");
        builder.Property(m => m.Reason).IsRequired().HasMaxLength(32).HasColumnType("VARCHAR(32)");
        builder.Property(m => m.ReferenceType).HasMaxLength(64).HasColumnType("VARCHAR(64)");
        builder.Property(m => m.Note).HasMaxLength(500).HasColumnType("NVARCHAR(500)");
        builder.Property(m => m.OccurredAtUtc).IsRequired();
        builder.Property(m => m.CreatedByUserId);

        builder.Property(m => m.CreatedAtUtc).IsRequired();
        builder.Property(m => m.UpdatedAtUtc);
        builder.Property(m => m.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(m => new { m.TenantId, m.ProductId, m.WarehouseId });
        builder.HasIndex(m => new { m.TenantId, m.ReferenceType, m.ReferenceId });

        // Idempotent posts: null-batch vs with-batch (multi-batch FEFO sale / refund restore).
        builder.HasIndex(m => new { m.TenantId, m.ReferenceType, m.ReferenceId, m.Reason })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [ReferenceId] IS NOT NULL AND [ReferenceType] IS NOT NULL AND [BatchId] IS NULL")
            .HasDatabaseName("UX_stock_movements_Idempotency_NoBatch");

        builder.HasIndex(m => new { m.TenantId, m.ReferenceType, m.ReferenceId, m.Reason, m.BatchId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [ReferenceId] IS NOT NULL AND [ReferenceType] IS NOT NULL AND [BatchId] IS NOT NULL")
            .HasDatabaseName("UX_stock_movements_Idempotency_WithBatch");

        builder.HasOne(m => m.Tenant).WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Product).WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Warehouse).WithMany().HasForeignKey(m => m.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}
