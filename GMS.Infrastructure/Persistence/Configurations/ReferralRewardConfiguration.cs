namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class ReferralRewardConfiguration : IEntityTypeConfiguration<ReferralReward>
{
    public void Configure(EntityTypeBuilder<ReferralReward> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.InvitationId).IsRequired();
        builder.Property(r => r.SaleId);
        builder.Property(r => r.BeneficiaryMemberId).IsRequired();
        builder.Property(r => r.BeneficiaryRole).IsRequired().HasMaxLength(10).HasColumnType("VARCHAR(10)");
        builder.Property(r => r.RewardType).IsRequired().HasMaxLength(10).HasColumnType("VARCHAR(10)");
        builder.Property(r => r.RewardValue).HasColumnType("DECIMAL(12,2)").IsRequired();
        builder.Property(r => r.Status).IsRequired().HasMaxLength(15).HasColumnType("VARCHAR(15)");
        builder.Property(r => r.HoldUntilUtc).HasColumnType("DATETIME2").IsRequired();
        builder.Property(r => r.GrantedAtUtc).HasColumnType("DATETIME2");
        builder.Property(r => r.ReversedAtUtc).HasColumnType("DATETIME2");
        builder.Property(r => r.ForfeitedAtUtc).HasColumnType("DATETIME2");
        builder.Property(r => r.CreditEntryId);
        builder.Property(r => r.ExtendedMembershipId);
        builder.Property(r => r.DaysGranted);
        builder.Property(r => r.IsFamily).HasDefaultValue(false);

        builder.Property(r => r.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();
        builder.Property(r => r.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(r => r.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(r => new { r.TenantId, r.Status, r.HoldUntilUtc });
        builder.HasIndex(r => new { r.TenantId, r.SaleId });
        builder.HasIndex(r => new { r.TenantId, r.InvitationId, r.BeneficiaryRole })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne(r => r.Tenant).WithMany()
            .HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Invitation).WithMany()
            .HasForeignKey(r => r.InvitationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.BeneficiaryMember).WithMany()
            .HasForeignKey(r => r.BeneficiaryMemberId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("referral_rewards", tb =>
        {
            tb.HasCheckConstraint("CK_referral_rewards_Status",
                "Status IN ('pending_hold','granted','reversed','forfeited')");
            tb.HasCheckConstraint("CK_referral_rewards_RewardType",
                "RewardType IN ('credit','free_days')");
            tb.HasCheckConstraint("CK_referral_rewards_BeneficiaryRole",
                "BeneficiaryRole IN ('referrer','referee')");
        });
    }
}
