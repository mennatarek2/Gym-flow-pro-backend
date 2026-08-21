namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class PromoCodeConfiguration : IEntityTypeConfiguration<PromoCode>
{
    public void Configure(EntityTypeBuilder<PromoCode> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(p => p.TenantId).IsRequired();

        builder.Property(p => p.Code).IsRequired().HasMaxLength(30).HasColumnType("NVARCHAR(30)");
        builder.Property(p => p.Type).IsRequired().HasMaxLength(10).HasColumnType("VARCHAR(10)");
        builder.Property(p => p.Value).HasColumnType("DECIMAL(12,2)").IsRequired();
        builder.Property(p => p.AppliesTo).HasColumnType("NVARCHAR(MAX)");

        builder.Property(p => p.ValidFrom).HasColumnType("DATE").IsRequired();
        builder.Property(p => p.ValidTo).HasColumnType("DATE").IsRequired();

        builder.Property(p => p.MaxUses);
        builder.Property(p => p.MaxUsesPerMember);
        builder.Property(p => p.UsesCount).HasDefaultValue(0).IsRequired();

        builder.Property(p => p.MinPrice).HasColumnType("DECIMAL(12,2)");
        builder.Property(p => p.IsActive).HasDefaultValue(true).IsRequired();

        builder.Property(p => p.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(p => p.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(p => p.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(p => new { p.TenantId, p.Code }).IsUnique();

        builder.HasOne(p => p.Tenant).WithMany()
            .HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("promo_codes");
    }
}
