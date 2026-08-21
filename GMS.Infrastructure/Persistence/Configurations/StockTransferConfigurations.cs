namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> builder)
    {
        builder.ToTable("stock_transfers");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.FromWarehouseId).IsRequired();
        builder.Property(t => t.ToWarehouseId).IsRequired();
        builder.Property(t => t.Status).IsRequired().HasMaxLength(32).HasColumnType("VARCHAR(32)");
        builder.Property(t => t.Note).HasMaxLength(1000).HasColumnType("NVARCHAR(1000)");
        builder.Property(t => t.CreatedByUserId).IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.HasIndex(t => new { t.TenantId, t.Status });
        builder.HasOne(t => t.Tenant).WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.FromWarehouse).WithMany().HasForeignKey(t => t.FromWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.ToWarehouse).WithMany().HasForeignKey(t => t.ToWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.CreatedByUser).WithMany().HasForeignKey(t => t.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.SubmittedByUser).WithMany().HasForeignKey(t => t.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.ReceivedByUser).WithMany().HasForeignKey(t => t.ReceivedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.CancelledByUser).WithMany().HasForeignKey(t => t.CancelledByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(t => t.Lines).WithOne(l => l.StockTransfer).HasForeignKey(l => l.StockTransferId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class StockTransferLineConfiguration : IEntityTypeConfiguration<StockTransferLine>
{
    public void Configure(EntityTypeBuilder<StockTransferLine> builder)
    {
        builder.ToTable("stock_transfer_lines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.StockTransferId).IsRequired();
        builder.Property(l => l.ProductId).IsRequired();
        builder.Property(l => l.Qty).IsRequired().HasColumnType("DECIMAL(18,3)");
        builder.Property(l => l.CreatedAtUtc).IsRequired();
        builder.Property(l => l.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.HasIndex(l => new { l.TenantId, l.StockTransferId });
        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Batch).WithMany().HasForeignKey(l => l.BatchId).OnDelete(DeleteBehavior.Restrict);
    }
}
