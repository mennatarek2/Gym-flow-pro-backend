namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.ShiftId).IsRequired();

        builder.Property(c => c.Type).IsRequired().HasMaxLength(12).HasColumnType("VARCHAR(12)");
        builder.Property(c => c.Amount).HasColumnType("DECIMAL(12,2)").IsRequired();
        builder.Property(c => c.ReferenceId);
        builder.Property(c => c.Reason).HasMaxLength(200).HasColumnType("NVARCHAR(200)");
        builder.Property(c => c.CreatedByUserId).IsRequired();

        builder.Property(c => c.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(c => c.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(c => c.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(c => new { c.TenantId, c.ShiftId });

        builder.HasOne(c => c.Tenant).WithMany()
            .HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);

        // Sale -> SaleLine precedent: a strict single-parent ownership relationship, so Cascade
        // here is safe (no competing cascade path), unlike the Tenant FK above.
        builder.HasOne(c => c.Shift).WithMany(s => s.Movements)
            .HasForeignKey(c => c.ShiftId).OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.CreatedByUser).WithMany()
            .HasForeignKey(c => c.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("cash_movements");
    }
}
