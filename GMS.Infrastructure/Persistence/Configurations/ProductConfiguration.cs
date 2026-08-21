namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.CategoryId);
        builder.Property(p => p.DefaultSupplierId);

        builder.Property(p => p.Sku).IsRequired().HasMaxLength(64).HasColumnType("VARCHAR(64)");
        builder.Property(p => p.Barcode).HasMaxLength(64).HasColumnType("VARCHAR(64)");
        builder.Property(p => p.Name).IsRequired().HasMaxLength(150).HasColumnType("NVARCHAR(150)");
        builder.Property(p => p.NameAr).HasMaxLength(150).HasColumnType("NVARCHAR(150)");
        builder.Property(p => p.Description).HasMaxLength(500).HasColumnType("NVARCHAR(500)");
        builder.Property(p => p.DescriptionAr).HasMaxLength(500).HasColumnType("NVARCHAR(500)");
        builder.Property(p => p.Brand).HasMaxLength(100).HasColumnType("NVARCHAR(100)");
        builder.Property(p => p.ImageUrl).HasMaxLength(500).HasColumnType("VARCHAR(500)");
        builder.Property(p => p.UnitOfMeasure).IsRequired().HasMaxLength(16).HasColumnType("VARCHAR(16)")
            .HasDefaultValue("pcs");

        builder.Property(p => p.SellPrice).IsRequired().HasColumnType("DECIMAL(12,2)");
        builder.Property(p => p.CostPrice).IsRequired().HasColumnType("DECIMAL(12,2)").HasDefaultValue(0m);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3).HasColumnType("CHAR(3)")
            .HasDefaultValue("EGP");
        builder.Property(p => p.Taxable).IsRequired().HasDefaultValue(true);
        builder.Property(p => p.VatRatePercent).HasColumnType("DECIMAL(5,2)");

        builder.Property(p => p.TrackStock).IsRequired().HasDefaultValue(true);
        builder.Property(p => p.TrackBatch).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.TrackExpiry).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.AllowFractionalQty).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.IsSellable).IsRequired().HasDefaultValue(true);
        builder.Property(p => p.IsPurchasable).IsRequired().HasDefaultValue(true);
        builder.Property(p => p.VisibleToMembers).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.ReorderMinQty).IsRequired().HasColumnType("DECIMAL(18,3)").HasDefaultValue(0m);

        builder.Property(p => p.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(p => p.IsArchived).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc);
        builder.Property(p => p.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(p => new { p.TenantId, p.Sku })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(p => new { p.TenantId, p.Barcode })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Barcode] IS NOT NULL");

        builder.HasIndex(p => new { p.TenantId, p.IsArchived, p.IsActive });
        builder.HasIndex(p => new { p.TenantId, p.VisibleToMembers, p.IsActive });
        builder.HasIndex(p => new { p.TenantId, p.DefaultSupplierId });

        builder.HasOne(p => p.Tenant)
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.DefaultSupplier)
            .WithMany()
            .HasForeignKey(p => p.DefaultSupplierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
