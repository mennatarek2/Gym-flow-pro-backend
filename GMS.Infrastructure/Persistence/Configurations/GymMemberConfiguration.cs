namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

/// <summary>
/// Entity Type Configuration for GymMember.
/// Gym member with multi-tenant isolation via tenant_id.
/// Includes encrypted national ID for privacy.
/// </summary>
public class GymMemberConfiguration : IEntityTypeConfiguration<GymMember>
{
    public void Configure(EntityTypeBuilder<GymMember> builder)
    {
        // Primary key
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        // Tenant context (CRITICAL for multi-tenancy)
        builder.Property(m => m.TenantId)
            .IsRequired();

        builder.HasIndex(m => m.TenantId)
            .IsUnique(false);

        // Member identification
        builder.Property(m => m.MemberNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(m => new { m.TenantId, m.MemberNumber })
            .IsUnique();

        builder.Property(m => m.FullName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(m => m.FullNameAr)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnType("NVARCHAR(200)");

        // Contact information
        builder.Property(m => m.PhoneNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(m => m.Email)
            .HasMaxLength(256);

        builder.HasIndex(m => new { m.TenantId, m.PhoneNumber })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        // Security & Identity
        builder.Property(m => m.NationalIdEncrypted)
            .IsRequired()
            .HasMaxLength(4000)
            .HasColumnType("NVARCHAR(MAX)");

        builder.Property(m => m.DateOfBirth)
            .HasColumnType("DATE")
            .IsRequired();

        // Profile
        builder.Property(m => m.ProfilePhotoUrl)
            .HasMaxLength(500);

        builder.Property(m => m.AppUserId)
            .HasColumnType("UNIQUEIDENTIFIER");

        builder.Property(m => m.Notes)
            .HasMaxLength(1000);

        builder.Property(m => m.IsActive)
            .HasDefaultValue(true);

        builder.Property(m => m.PaperWaiverOnFile)
            .IsRequired()
            .HasColumnType("BIT")
            .HasDefaultValue(false);

        // Trial tracking
        builder.Property(m => m.IsTrial)
            .IsRequired()
            .HasColumnType("BIT")
            .HasDefaultValue(false);

        builder.Property(m => m.TrialOutcome)
            .HasMaxLength(12)
            .HasColumnType("VARCHAR(12)");

        builder.Property(m => m.TrialConvertedAt)
            .HasColumnType("DATETIME2");

        builder.Property(m => m.ConvertingSaleId)
            .HasColumnType("UNIQUEIDENTIFIER");

        // Invitations
        builder.Property(m => m.InvitationQuotaRemaining)
            .HasDefaultValue(0);

        builder.Property(m => m.LastInvitationResetDate)
            .HasColumnType("DATETIME2");

        builder.Property(m => m.ReferralCode)
            .HasMaxLength(20)
            .HasColumnType("NVARCHAR(20)");

        builder.HasIndex(m => new { m.TenantId, m.ReferralCode })
            .IsUnique()
            .HasFilter("[ReferralCode] IS NOT NULL AND [IsDeleted] = 0");

        builder.Property(m => m.SuccessfulReferralCount)
            .HasDefaultValue(0);

        builder.Property(m => m.ReferralTier)
            .HasMaxLength(15)
            .HasColumnType("VARCHAR(15)");

        // Timestamps
        builder.Property(m => m.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(m => m.UpdatedAtUtc)
            .HasColumnType("DATETIME2");

        builder.Property(m => m.IsDeleted)
            .HasDefaultValue(false);

        // Indexes for queries
        builder.HasIndex(m => new { m.TenantId, m.IsActive });
        builder.HasIndex(m => new { m.TenantId, m.IsDeleted });

        // Navigation
        builder.HasOne(m => m.Tenant)
            .WithMany(t => t.Members)
            .HasForeignKey(m => m.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.AppUser)
            .WithOne(u => u.LinkedMember)
            .HasForeignKey<GymMember>(m => m.AppUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(m => m.Memberships)
            .WithOne(ms => ms.Member)
            .HasForeignKey(ms => ms.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.Attendances)
            .WithOne(a => a.Member)
            .HasForeignKey(a => a.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict (not Cascade) to avoid SQL Server error 1785:
        // multiple cascade paths from GymMember → MemberInvitation.
        builder.HasMany(m => m.InvitationsSent)
            .WithOne(i => i.InvitingMember)
            .HasForeignKey(i => i.InvitingMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(m => m.InvitationsConverted)
            .WithOne(i => i.ConvertedMember)
            .HasForeignKey(i => i.ConvertedMemberId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(m => m.ConvertingSale)
            .WithMany()
            .HasForeignKey(m => m.ConvertingSaleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ToTable("gym_members", tb =>
            tb.HasCheckConstraint("CK_gym_members_TrialOutcome",
                "TrialOutcome IS NULL OR TrialOutcome IN ('active_trial','converted','expired')"));
    }
}
