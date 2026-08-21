namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
{
    public void Configure(EntityTypeBuilder<StockCount> builder)
    {
        builder.ToTable("stock_counts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.WarehouseId).IsRequired();
        builder.Property(c => c.Status).IsRequired().HasMaxLength(32).HasColumnType("VARCHAR(32)");
        builder.Property(c => c.CountedAtUtc).IsRequired();
        builder.Property(c => c.Note).HasMaxLength(1000).HasColumnType("NVARCHAR(1000)");
        builder.Property(c => c.CreatedByUserId).IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.HasIndex(c => new { c.TenantId, c.Status });
        builder.HasOne(c => c.Tenant).WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Warehouse).WithMany().HasForeignKey(c => c.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.CreatedByUser).WithMany().HasForeignKey(c => c.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.SubmittedByUser).WithMany().HasForeignKey(c => c.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.ApprovedByUser).WithMany().HasForeignKey(c => c.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(c => c.Lines).WithOne(l => l.StockCount).HasForeignKey(l => l.StockCountId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class StockCountLineConfiguration : IEntityTypeConfiguration<StockCountLine>
{
    public void Configure(EntityTypeBuilder<StockCountLine> builder)
    {
        builder.ToTable("stock_count_lines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.StockCountId).IsRequired();
        builder.Property(l => l.ProductId).IsRequired();
        builder.Property(l => l.SystemQty).IsRequired().HasColumnType("DECIMAL(18,3)");
        builder.Property(l => l.CountedQty).IsRequired().HasColumnType("DECIMAL(18,3)");
        builder.Property(l => l.Variance).IsRequired().HasColumnType("DECIMAL(18,3)");
        builder.Property(l => l.CreatedAtUtc).IsRequired();
        builder.Property(l => l.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.HasIndex(l => new { l.TenantId, l.StockCountId });
        builder.HasIndex(l => new { l.StockCountId, l.ProductId }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
