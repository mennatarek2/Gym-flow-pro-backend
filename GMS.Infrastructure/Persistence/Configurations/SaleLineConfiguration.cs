namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.SaleId).IsRequired();

        builder.Property(l => l.LineType).IsRequired().HasMaxLength(15).HasColumnType("VARCHAR(15)");
        builder.Property(l => l.ReferenceId);

        builder.Property(l => l.Description).IsRequired().HasMaxLength(300).HasColumnType("NVARCHAR(300)");
        builder.Property(l => l.DescriptionAr).HasMaxLength(300).HasColumnType("NVARCHAR(300)");

        builder.Property(l => l.Qty).HasDefaultValue(1).IsRequired();
        builder.Property(l => l.UnitPrice).HasColumnType("DECIMAL(12,2)").IsRequired();
        builder.Property(l => l.LineTotal).HasColumnType("DECIMAL(12,2)").IsRequired();
        builder.Property(l => l.UnitCost).HasColumnType("DECIMAL(12,2)");
        builder.Property(l => l.CogsAmount).HasColumnType("DECIMAL(14,2)");

        builder.Property(l => l.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(l => l.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(l => l.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(l => new { l.TenantId, l.SaleId });

        builder.HasOne(l => l.Tenant).WithMany()
            .HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);

        // Sale -> SaleLine is a strict single-parent ownership relationship (no competing cascade
        // path), unlike the Tenant FK above, so Cascade is safe here — mirrors GymMember -> Membership.
        builder.HasOne(l => l.Sale).WithMany(s => s.Lines)
            .HasForeignKey(l => l.SaleId).OnDelete(DeleteBehavior.Cascade);

        builder.ToTable("sale_lines");
    }
}
