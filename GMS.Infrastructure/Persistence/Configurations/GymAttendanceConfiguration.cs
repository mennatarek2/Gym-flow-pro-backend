namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

/// <summary>
/// Entity Type Configuration for GymAttendance.
/// Records member check-in/check-out events.
/// Supports both QR code and manual entry methods.
/// </summary>
public class GymAttendanceConfiguration : IEntityTypeConfiguration<GymAttendance>
{
    public void Configure(EntityTypeBuilder<GymAttendance> builder)
    {
        // Primary key
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        // Tenant context
        builder.Property(a => a.TenantId)
            .IsRequired();

        builder.HasIndex(a => a.TenantId)
            .IsUnique(false);

        // Foreign keys
        builder.Property(a => a.MemberId)
            .IsRequired(false);

        builder.Property(a => a.GuestName)
            .HasMaxLength(200)
            .HasColumnType("NVARCHAR(200)");

        builder.Property(a => a.GuestPhone)
            .HasMaxLength(30)
            .HasColumnType("NVARCHAR(30)");

        builder.Property(a => a.MembershipId)
            .HasColumnType("UNIQUEIDENTIFIER");

        builder.Property(a => a.BookingId)
            .HasColumnType("UNIQUEIDENTIFIER");

        builder.Property(a => a.SessionId)
            .HasColumnType("UNIQUEIDENTIFIER");

        builder.Property(a => a.StaffUserId)
            .HasColumnType("UNIQUEIDENTIFIER");

        builder.HasIndex(a => new { a.TenantId, a.MemberId });
        builder.HasIndex(a => new { a.TenantId, a.CheckInAtUtc });

        // Check-in/out
        builder.Property(a => a.CheckInAtUtc)
            .HasColumnType("DATETIME2")
            .IsRequired();

        builder.Property(a => a.CheckOutAtUtc)
            .HasColumnType("DATETIME2");

        // Entry method
        builder.Property(a => a.EntryMethod)
            .IsRequired()
            .HasMaxLength(10)
            .HasColumnType("VARCHAR(10)")
            .HasDefaultValue("qr");

        // Manual entry reason
        builder.Property(a => a.ManualReason)
            .HasMaxLength(100)
            .HasColumnType("NVARCHAR(100)");

        // Duration
        builder.Property(a => a.Duration)
            .HasColumnType("TIME");

        builder.Property(a => a.PresenceStatus)
            .HasMaxLength(12)
            .HasColumnType("VARCHAR(12)");

        builder.Property(a => a.DeviceFingerprint)
            .HasMaxLength(100)
            .HasColumnType("NVARCHAR(100)");

        // Timestamps
        builder.Property(a => a.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(a => a.UpdatedAtUtc)
            .HasColumnType("DATETIME2");

        builder.Property(a => a.IsDeleted)
            .HasDefaultValue(false);

        // Navigation
        builder.HasOne(a => a.Tenant)
            .WithMany(t => t.Attendances)
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Member)
            .WithMany(m => m.Attendances)
            .HasForeignKey(a => a.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict (not Cascade) to avoid SQL Server error 1785:
        // multiple cascade paths via GymMember → Membership → Attendance
        // AND GymMember → Attendance.
        builder.HasOne(a => a.Membership)
            .WithMany(ms => ms.Attendances)
            .HasForeignKey(a => a.MembershipId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.StaffUser)
            .WithMany(u => u.RecordedAttendances)
            .HasForeignKey(a => a.StaffUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(a => a.Session)
            .WithMany()
            .HasForeignKey(a => a.SessionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ToTable("gym_attendance");
    }
}
