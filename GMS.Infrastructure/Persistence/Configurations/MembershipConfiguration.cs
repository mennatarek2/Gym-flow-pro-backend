namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

/// <summary>
/// Entity Type Configuration for Membership.
/// Tracks member's active/inactive membership to a plan.
/// Supports frozen memberships, auto-renewal, and session counting.
/// </summary>
public class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        // Primary key
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        // Tenant context
        builder.Property(m => m.TenantId)
            .IsRequired();

        builder.HasIndex(m => m.TenantId)
            .IsUnique(false);

        // Foreign keys
        builder.Property(m => m.MemberId)
            .IsRequired();

        builder.Property(m => m.PlanId)
            .IsRequired();

        builder.HasIndex(m => new { m.TenantId, m.MemberId });

        // Membership dates
        builder.Property(m => m.StartDate)
            .HasColumnType("DATE")
            .IsRequired();

        builder.Property(m => m.EndDate)
            .HasColumnType("DATE")
            .IsRequired();

        // Status
        builder.Property(m => m.Status)
            .IsRequired()
            .HasMaxLength(15)
            .HasColumnType("VARCHAR(15)")
            .HasDefaultValue("active");

        builder.HasIndex(m => new { m.TenantId, m.Status });

        // Sessions
        builder.Property(m => m.SessionsRemaining)
            .HasColumnType("INT");

        // Freeze tracking
        builder.Property(m => m.FrozenFromDate)
            .HasColumnType("DATE");

        builder.Property(m => m.FrozenUntilDate)
            .HasColumnType("DATE");

        // Payment
        builder.Property(m => m.PaymentMethod)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("cash");

        builder.Property(m => m.AmountPaid)
            .HasColumnType("DECIMAL(12,2)")
            .IsRequired();

        builder.Property(m => m.PaymentDate)
            .HasColumnType("DATETIME2");

        // Auto-renewal
        builder.Property(m => m.AutoRenew)
            .HasDefaultValue(false);

        builder.Property(m => m.LastRenewalDate)
            .HasColumnType("DATETIME2");

        builder.Property(m => m.PlanTransitionMode)
            .HasMaxLength(32)
            .HasColumnType("VARCHAR(32)");

        // Timestamps
        builder.Property(m => m.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(m => m.UpdatedAtUtc)
            .HasColumnType("DATETIME2");

        builder.Property(m => m.IsDeleted)
            .HasDefaultValue(false);

        // Indexes
        builder.HasIndex(m => new { m.TenantId, m.IsDeleted });
        builder.HasIndex(m => new { m.TenantId, m.Status, m.EndDate });

        // Navigation
        builder.HasOne(m => m.Tenant)
            .WithMany(t => t.Memberships)
            .HasForeignKey(m => m.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Member)
            .WithMany(gm => gm.Memberships)
            .HasForeignKey(m => m.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Plan)
            .WithMany(p => p.Memberships)
            .HasForeignKey(m => m.PlanId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict (not Cascade) to avoid SQL Server error 1785:
        // multiple cascade paths via GymMember → Membership → Attendance.
        builder.HasMany(m => m.Attendances)
            .WithOne(a => a.Membership)
            .HasForeignKey(a => a.MembershipId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("memberships");
    }
}
