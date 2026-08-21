namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.UserId).IsRequired();

        builder.Property(s => s.OpenedAt).HasColumnType("DATETIME2").IsRequired();
        builder.Property(s => s.ClosedAt).HasColumnType("DATETIME2");

        builder.Property(s => s.OpeningFloat).HasColumnType("DECIMAL(12,2)").IsRequired();
        builder.Property(s => s.ExpectedCash).HasColumnType("DECIMAL(12,2)");
        builder.Property(s => s.CountedCash).HasColumnType("DECIMAL(12,2)");
        builder.Property(s => s.Variance).HasColumnType("DECIMAL(12,2)");
        builder.Property(s => s.VarianceNote).HasMaxLength(300).HasColumnType("NVARCHAR(300)");

        builder.Property(s => s.Status)
            .IsRequired().HasMaxLength(10).HasColumnType("VARCHAR(10)").HasDefaultValue("open");

        builder.Property(s => s.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(s => s.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(s => s.IsDeleted).HasDefaultValue(false);

        // One open shift per user per tenant.
        builder.HasIndex(s => new { s.TenantId, s.UserId })
            .IsUnique()
            .HasFilter("[Status] = 'open'");

        builder.HasIndex(s => new { s.TenantId, s.Status });

        builder.HasOne(s => s.Tenant).WithMany()
            .HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.User).WithMany()
            .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.ApprovedByUser).WithMany()
            .HasForeignKey(s => s.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("shifts");
    }
}
