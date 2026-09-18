namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Constants;
using GMS.Core.Entities;

public class AccessCardConfiguration : IEntityTypeConfiguration<AccessCard>
{
    public void Configure(EntityTypeBuilder<AccessCard> builder)
    {
        builder.ToTable("access_cards", tb =>
        {
            tb.HasCheckConstraint(
                "CK_access_cards_Status",
                "Status IN ('Available','Assigned','Lost','Damaged','Blocked')");
        });

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.Code)
            .IsRequired()
            .HasMaxLength(40)
            .HasColumnType("VARCHAR(40)");
        builder.Property(c => c.Status)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnType("VARCHAR(20)")
            .HasDefaultValue(AccessCardStatuses.Available);
        builder.Property(c => c.AssignedAtUtc).HasColumnType("DATETIME2");
        builder.Property(c => c.LostAtUtc).HasColumnType("DATETIME2");
        builder.Property(c => c.Reason).HasMaxLength(240).HasColumnType("NVARCHAR(240)");
        builder.Property(c => c.BatchId).HasMaxLength(40).HasColumnType("VARCHAR(40)");

        builder.Property(c => c.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();
        builder.Property(c => c.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(c => c.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(c => new { c.TenantId, c.Code })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        // One assigned card per member at a time.
        builder.HasIndex(c => new { c.TenantId, c.MemberId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Status] = 'Assigned' AND [MemberId] IS NOT NULL");

        builder.HasIndex(c => new { c.TenantId, c.Status });
        builder.HasIndex(c => new { c.TenantId, c.BatchId });

        builder.HasOne(c => c.Tenant).WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Member).WithMany()
            .HasForeignKey(c => c.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
