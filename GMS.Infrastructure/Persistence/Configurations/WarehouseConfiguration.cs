namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("warehouses");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(w => w.TenantId).IsRequired();
        builder.Property(w => w.Code).IsRequired().HasMaxLength(32).HasColumnType("VARCHAR(32)");
        builder.Property(w => w.Name).IsRequired().HasMaxLength(150).HasColumnType("NVARCHAR(150)");
        builder.Property(w => w.NameAr).HasMaxLength(150).HasColumnType("NVARCHAR(150)");
        builder.Property(w => w.IsDefault).IsRequired().HasDefaultValue(false);
        builder.Property(w => w.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(w => w.BranchId);

        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.UpdatedAtUtc);
        builder.Property(w => w.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(w => new { w.TenantId, w.Code })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        // At most one default warehouse per tenant among live rows.
        builder.HasIndex(w => w.TenantId)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [IsDefault] = 1")
            .HasDatabaseName("IX_warehouses_TenantId_Default");

        builder.HasOne(w => w.Tenant)
            .WithMany()
            .HasForeignKey(w => w.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
