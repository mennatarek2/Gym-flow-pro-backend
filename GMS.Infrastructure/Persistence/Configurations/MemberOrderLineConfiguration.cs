namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class MemberOrderLineConfiguration : IEntityTypeConfiguration<MemberOrderLine>
{
    public void Configure(EntityTypeBuilder<MemberOrderLine> builder)
    {
        builder.ToTable("member_order_lines");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.MemberOrderId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();

        builder.Property(x => x.ProductSku)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnType("VARCHAR(64)");

        builder.Property(x => x.ProductName)
            .IsRequired()
            .HasMaxLength(150)
            .HasColumnType("NVARCHAR(150)");

        builder.Property(x => x.ProductNameAr)
            .HasMaxLength(150)
            .HasColumnType("NVARCHAR(150)");

        builder.Property(x => x.UnitPrice).IsRequired().HasColumnType("DECIMAL(12,2)");
        builder.Property(x => x.Qty).IsRequired().HasColumnType("DECIMAL(18,3)");
        builder.Property(x => x.LineTotal).IsRequired().HasColumnType("DECIMAL(12,2)");
        builder.Property(x => x.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .HasColumnType("CHAR(3)")
            .HasDefaultValue("EGP");

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc);
        builder.Property(x => x.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(x => new { x.TenantId, x.MemberOrderId });

        builder.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
