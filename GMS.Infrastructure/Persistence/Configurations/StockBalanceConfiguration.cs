namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> builder)
    {
        builder.ToTable("stock_balances");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(b => b.TenantId).IsRequired();
        builder.Property(b => b.ProductId).IsRequired();
        builder.Property(b => b.WarehouseId).IsRequired();
        builder.Property(b => b.BatchId);
        builder.Property(b => b.QtyOnHand).IsRequired().HasColumnType("DECIMAL(18,3)");
        builder.Property(b => b.RowVersion).IsRowVersion();

        builder.Property(b => b.CreatedAtUtc).IsRequired();
        builder.Property(b => b.UpdatedAtUtc);
        builder.Property(b => b.IsDeleted).IsRequired().HasDefaultValue(false);

        // SQL Server unique allows multiple NULLs — use two filtered indexes.
        builder.HasIndex(b => new { b.TenantId, b.ProductId, b.WarehouseId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [BatchId] IS NULL")
            .HasDatabaseName("UX_stock_balances_NoBatch");

        builder.HasIndex(b => new { b.TenantId, b.ProductId, b.WarehouseId, b.BatchId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [BatchId] IS NOT NULL")
            .HasDatabaseName("UX_stock_balances_WithBatch");

        builder.HasOne(b => b.Tenant).WithMany().HasForeignKey(b => b.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.Product).WithMany().HasForeignKey(b => b.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.Warehouse).WithMany().HasForeignKey(b => b.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}
