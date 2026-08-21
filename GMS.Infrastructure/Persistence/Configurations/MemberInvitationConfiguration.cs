namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

/// <summary>
/// Entity Type Configuration for MemberInvitation.
/// Tracks guest invitations sent by members.
/// Supports quota management and conversion tracking for business analytics.
/// </summary>
public class MemberInvitationConfiguration : IEntityTypeConfiguration<MemberInvitation>
{
    public void Configure(EntityTypeBuilder<MemberInvitation> builder)
    {
        // Primary key
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        // Tenant context
        builder.Property(i => i.TenantId)
            .IsRequired();

        builder.HasIndex(i => i.TenantId)
            .IsUnique(false);

        // Foreign keys
        builder.Property(i => i.InvitingMemberId)
            .IsRequired();

        builder.Property(i => i.ConvertedMemberId)
            .HasColumnType("UNIQUEIDENTIFIER");

        builder.Property(i => i.InvitationType)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnType("VARCHAR(20)")
            .HasDefaultValue("invitation");

        builder.Property(i => i.CoveringMembershipId)
            .HasColumnType("UNIQUEIDENTIFIER");

        builder.Property(i => i.NationalIdEncrypted)
            .HasColumnType("NVARCHAR(MAX)");

        builder.Property(i => i.Notes)
            .HasMaxLength(1000)
            .HasColumnType("NVARCHAR(1000)");

        builder.Property(i => i.ContactedAtUtc)
            .HasColumnType("DATETIME2");

        builder.HasIndex(i => new { i.TenantId, i.InvitingMemberId });
        builder.HasIndex(i => new { i.TenantId, i.Status });
        builder.HasIndex(i => new { i.TenantId, i.InvitationType });
        builder.HasIndex(i => new { i.TenantId, i.GuestPhoneNumber, i.InvitationType });
        builder.HasIndex(i => new { i.TenantId, i.CoveringMembershipId });

        // Guest information
        builder.Property(i => i.GuestName)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnType("NVARCHAR(200)");

        builder.Property(i => i.GuestPhoneNumber)
            .IsRequired()
            .HasMaxLength(20);

        // Visit tracking (nullable for referral rows)
        builder.Property(i => i.VisitDate)
            .HasColumnType("DATE")
            .IsRequired(false);

        builder.Property(i => i.Status)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnType("VARCHAR(20)")
            .HasDefaultValue("pending");

        builder.Property(i => i.ReferralCodeUsed)
            .HasMaxLength(20)
            .HasColumnType("NVARCHAR(20)");

        builder.Property(i => i.ConvertingSaleId)
            .HasColumnType("UNIQUEIDENTIFIER");

        // Quota period (YYYY-MM format)
        builder.Property(i => i.QuotaPeriod)
            .IsRequired()
            .HasMaxLength(7)
            .HasColumnType("NVARCHAR(7)");

        // Timeline
        builder.Property(i => i.SentAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(i => i.VisitedAtUtc)
            .HasColumnType("DATETIME2");

        builder.Property(i => i.ConvertedAtUtc)
            .HasColumnType("DATETIME2");

        // Timestamps
        builder.Property(i => i.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(i => i.UpdatedAtUtc)
            .HasColumnType("DATETIME2");

        builder.Property(i => i.IsDeleted)
            .HasDefaultValue(false);

        // Indexes
        builder.HasIndex(i => new { i.TenantId, i.QuotaPeriod });
        builder.HasIndex(i => new { i.TenantId, i.IsDeleted });

        // Navigation
        builder.HasOne(i => i.Tenant)
            .WithMany(t => t.Invitations)
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict (not Cascade) to avoid SQL Server error 1785:
        // multiple cascade paths from GymMember to MemberInvitation
        // (InvitingMemberId + ConvertedMemberId both reference GymMember).
        // Soft delete is used, so cascade delete is not needed.
        builder.HasOne(i => i.InvitingMember)
            .WithMany(m => m.InvitationsSent)
            .HasForeignKey(i => i.InvitingMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.ConvertedMember)
            .WithMany(m => m.InvitationsConverted)
            .HasForeignKey(i => i.ConvertedMemberId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ToTable("member_invitations");
    }
}
