namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class SaleIdempotencyKeyConfiguration : IEntityTypeConfiguration<SaleIdempotencyKey>
{
    public void Configure(EntityTypeBuilder<SaleIdempotencyKey> builder)
    {
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(k => k.TenantId).IsRequired();
        builder.Property(k => k.Key).IsRequired().HasMaxLength(100).HasColumnType("NVARCHAR(100)");
        builder.Property(k => k.SaleId).IsRequired();
        builder.Property(k => k.ResponseHash).IsRequired().HasMaxLength(64).HasColumnType("NVARCHAR(64)");

        builder.Property(k => k.CreatedAt)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.HasIndex(k => new { k.TenantId, k.Key }).IsUnique();

        builder.HasOne(k => k.Sale).WithMany()
            .HasForeignKey(k => k.SaleId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("sale_idempotency_keys");
    }
}
