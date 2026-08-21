namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

/// <summary>
/// Entity Type Configuration for MembershipPlan.
/// Defines gym membership plan types and pricing.
/// Supports multiple plan models: unlimited, session packs, time-limited, PT credits, family.
/// </summary>
public class MembershipPlanConfiguration : IEntityTypeConfiguration<MembershipPlan>
{
    public void Configure(EntityTypeBuilder<MembershipPlan> builder)
    {
        // Primary key
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        // Tenant context
        builder.Property(p => p.TenantId)
            .IsRequired();

        builder.HasIndex(p => p.TenantId)
            .IsUnique(false);

        // Plan information
        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(p => p.NameAr)
            .IsRequired()
            .HasMaxLength(150)
            .HasColumnType("NVARCHAR(150)");

        builder.Property(p => p.Description)
            .HasMaxLength(500);

        builder.Property(p => p.DescriptionAr)
            .HasMaxLength(500)
            .HasColumnType("NVARCHAR(500)");

        // Plan type
        builder.Property(p => p.PlanType)
            .IsRequired()
            .HasMaxLength(30)
            .HasColumnType("VARCHAR(30)")
            .HasDefaultValue("monthly_unlimited");

        // Duration & Sessions
        builder.Property(p => p.DurationDays)
            .IsRequired();

        builder.Property(p => p.SessionCount)
            .HasColumnType("INT");

        builder.Property(p => p.TrialVisitLimit)
            .HasColumnType("INT");

        // Pricing
        builder.Property(p => p.Price)
            .IsRequired()
            .HasColumnType("DECIMAL(12,2)");

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .HasDefaultValue("EGP");

        // Time restrictions
        builder.Property(p => p.TimeRestrictionStart)
            .HasColumnType("TIME");

        builder.Property(p => p.TimeRestrictionEnd)
            .HasColumnType("TIME");

        // Guest invitations (retired) + Invitation product quota
        builder.Property(p => p.InvitationQuota)
            .HasDefaultValue(0);

        builder.Property(p => p.ReferralInviteQuota)
            .HasDefaultValue(0);

        builder.Property(p => p.ReferralRewardType)
            .HasMaxLength(10)
            .HasColumnType("VARCHAR(10)");

        builder.Property(p => p.ReferralRewardValue)
            .HasColumnType("DECIMAL(12,2)");

        // Status
        builder.Property(p => p.IsActive)
            .HasDefaultValue(true);

        // Timestamps
        builder.Property(p => p.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(p => p.UpdatedAtUtc)
            .HasColumnType("DATETIME2");

        builder.Property(p => p.IsDeleted)
            .HasDefaultValue(false);

        // Indexes
        builder.HasIndex(p => new { p.TenantId, p.IsActive });
        builder.HasIndex(p => new { p.TenantId, p.PlanType });

        // Navigation
        builder.HasOne(p => p.Tenant)
            .WithMany(t => t.MembershipPlans)
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Memberships)
            .WithOne(m => m.Plan)
            .HasForeignKey(m => m.PlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("membership_plans", tb =>
            tb.HasCheckConstraint("CK_membership_plans_PlanType",
                "PlanType IN ('monthly_unlimited','session_pack','time_limited','pt_credits','family','trial','day_pass')"));
    }
}
