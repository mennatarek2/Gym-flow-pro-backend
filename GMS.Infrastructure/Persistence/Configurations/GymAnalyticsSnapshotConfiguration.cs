namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

/// <summary>
/// EF configuration for GymAnalyticsSnapshot.
/// Unique constraint on (TenantId, SnapshotDate) for UPSERT pattern.
/// </summary>
public class GymAnalyticsSnapshotConfiguration : IEntityTypeConfiguration<GymAnalyticsSnapshot>
{
    public void Configure(EntityTypeBuilder<GymAnalyticsSnapshot> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.SnapshotDate).HasColumnType("DATE").IsRequired();

        // Unique: one snapshot per tenant per day
        builder.HasIndex(s => new { s.TenantId, s.SnapshotDate }).IsUnique();

        builder.Property(s => s.ActiveMembers).HasDefaultValue(0);
        builder.Property(s => s.ExpiredMembers).HasDefaultValue(0);
        builder.Property(s => s.FrozenMembers).HasDefaultValue(0);
        builder.Property(s => s.NewMembersThisMonth).HasDefaultValue(0);

        builder.Property(s => s.RevenueThisMonth)
            .HasColumnType("DECIMAL(14,2)")
            .HasDefaultValue(0m);

        builder.Property(s => s.Currency)
            .HasMaxLength(5)
            .HasDefaultValue("EGP");

        builder.Property(s => s.CheckinsToday).HasDefaultValue(0);
        builder.Property(s => s.CheckinsThisMonth).HasDefaultValue(0);
        builder.Property(s => s.InvitationsSentThisMonth).HasDefaultValue(0);
        builder.Property(s => s.InvitationsConvertedThisMonth).HasDefaultValue(0);

        builder.Property(s => s.IsDeleted).HasDefaultValue(false);

        builder.HasOne(s => s.Tenant)
            .WithMany()
            .HasForeignKey(s => s.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("gym_analytics_snapshots");
    }
}
