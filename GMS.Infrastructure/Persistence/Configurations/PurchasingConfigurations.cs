namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200).HasColumnType("NVARCHAR(200)");
        builder.Property(s => s.NameAr).HasMaxLength(200).HasColumnType("NVARCHAR(200)");
        builder.Property(s => s.Phone).HasMaxLength(30).HasColumnType("VARCHAR(30)");
        builder.Property(s => s.Email).HasMaxLength(200).HasColumnType("VARCHAR(200)");
        builder.Property(s => s.PaymentTerms).HasMaxLength(200).HasColumnType("NVARCHAR(200)");
        builder.Property(s => s.Notes).HasMaxLength(1000).HasColumnType("NVARCHAR(1000)");
        builder.Property(s => s.Address).HasMaxLength(500).HasColumnType("NVARCHAR(500)");
        builder.Property(s => s.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.HasIndex(s => new { s.TenantId, s.Name });
        builder.HasOne(s => s.Tenant).WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.SupplierId).IsRequired();
        builder.Property(p => p.WarehouseId).IsRequired();
        builder.Property(p => p.Status).IsRequired().HasMaxLength(32).HasColumnType("VARCHAR(32)");
        builder.Property(p => p.OrderedAtUtc).IsRequired();
        builder.Property(p => p.Notes).HasMaxLength(1000).HasColumnType("NVARCHAR(1000)");
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.HasIndex(p => new { p.TenantId, p.Status });
        builder.HasIndex(p => new { p.TenantId, p.SupplierId });
        builder.HasOne(p => p.Tenant).WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Supplier).WithMany(s => s.PurchaseOrders).HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Warehouse).WithMany().HasForeignKey(p => p.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.ApprovedByUser).WithMany().HasForeignKey(p => p.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(p => p.Lines).WithOne(l => l.PurchaseOrder).HasForeignKey(l => l.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("purchase_order_lines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.PurchaseOrderId).IsRequired();
        builder.Property(l => l.ProductId).IsRequired();
        builder.Property(l => l.QtyOrdered).IsRequired().HasColumnType("DECIMAL(18,3)");
        builder.Property(l => l.QtyReceived).IsRequired().HasColumnType("DECIMAL(18,3)").HasDefaultValue(0m);
        builder.Property(l => l.UnitCost).IsRequired().HasColumnType("DECIMAL(14,2)");
        builder.Property(l => l.CreatedAtUtc).IsRequired();
        builder.Property(l => l.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.HasIndex(l => new { l.TenantId, l.PurchaseOrderId });
        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.ToTable("goods_receipts");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(g => g.TenantId).IsRequired();
        builder.Property(g => g.PurchaseOrderId).IsRequired();
        builder.Property(g => g.WarehouseId).IsRequired();
        builder.Property(g => g.ReceivedAtUtc).IsRequired();
        builder.Property(g => g.ReceivedByUserId).IsRequired();
        builder.Property(g => g.CreatedAtUtc).IsRequired();
        builder.Property(g => g.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.HasIndex(g => new { g.TenantId, g.PurchaseOrderId });
        builder.HasOne(g => g.Tenant).WithMany().HasForeignKey(g => g.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(g => g.PurchaseOrder).WithMany(p => p.GoodsReceipts).HasForeignKey(g => g.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(g => g.Warehouse).WithMany().HasForeignKey(g => g.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(g => g.ReceivedByUser).WithMany().HasForeignKey(g => g.ReceivedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(g => g.Lines).WithOne(l => l.GoodsReceipt).HasForeignKey(l => l.GoodsReceiptId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> builder)
    {
        builder.ToTable("goods_receipt_lines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.GoodsReceiptId).IsRequired();
        builder.Property(l => l.PurchaseOrderLineId).IsRequired();
        builder.Property(l => l.ProductId).IsRequired();
        builder.Property(l => l.Qty).IsRequired().HasColumnType("DECIMAL(18,3)");
        builder.Property(l => l.UnitCost).IsRequired().HasColumnType("DECIMAL(14,2)");
        builder.Property(l => l.BatchNumber).HasMaxLength(64).HasColumnType("VARCHAR(64)");
        builder.Property(l => l.ExpiresOn).HasColumnType("date");
        builder.Property(l => l.CreatedAtUtc).IsRequired();
        builder.Property(l => l.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.HasOne(l => l.PurchaseOrderLine).WithMany().HasForeignKey(l => l.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.ProductBatch).WithMany().HasForeignKey(l => l.ProductBatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductBatchConfiguration : IEntityTypeConfiguration<ProductBatch>
{
    public void Configure(EntityTypeBuilder<ProductBatch> builder)
    {
        builder.ToTable("product_batches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(b => b.TenantId).IsRequired();
        builder.Property(b => b.ProductId).IsRequired();
        builder.Property(b => b.BatchNumber).IsRequired().HasMaxLength(64).HasColumnType("VARCHAR(64)");
        builder.Property(b => b.ExpiresOn).HasColumnType("date");
        builder.Property(b => b.CreatedAtUtc).IsRequired();
        builder.Property(b => b.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.HasIndex(b => new { b.TenantId, b.ProductId, b.BatchNumber })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasOne(b => b.Tenant).WithMany().HasForeignKey(b => b.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.Product).WithMany().HasForeignKey(b => b.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SupplierLedgerEntryConfiguration : IEntityTypeConfiguration<SupplierLedgerEntry>
{
    public void Configure(EntityTypeBuilder<SupplierLedgerEntry> builder)
    {
        builder.ToTable("supplier_ledger_entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.SupplierId).IsRequired();
        builder.Property(e => e.Amount).IsRequired().HasColumnType("DECIMAL(14,2)");
        builder.Property(e => e.Reason).IsRequired().HasMaxLength(32).HasColumnType("VARCHAR(32)");
        builder.Property(e => e.ReferenceType).HasMaxLength(64).HasColumnType("VARCHAR(64)");
        builder.Property(e => e.Note).HasMaxLength(500).HasColumnType("NVARCHAR(500)");
        builder.Property(e => e.EffectiveAtUtc).HasColumnType("DATETIME2");
        builder.Property(e => e.CreatedAtUtc).IsRequired();
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.HasIndex(e => new { e.TenantId, e.SupplierId });
        builder.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Supplier).WithMany().HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);
    }
}
