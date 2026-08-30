namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(a => a.Name).IsRequired().HasMaxLength(120);
        builder.Property(a => a.NameAr).HasMaxLength(120);
        builder.Property(a => a.Description).HasMaxLength(500);
        builder.Property(a => a.DescriptionAr).HasMaxLength(500);
        builder.Property(a => a.Kind).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(a => a.SystemKey).HasMaxLength(40).HasColumnType("VARCHAR(40)");
        builder.Property(a => a.DropInPrice).HasColumnType("DECIMAL(12,2)");
        builder.HasOne(a => a.Tenant).WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.TenantId, a.SystemKey });
        builder.ToTable("activities");
    }
}

public class ActivityScheduleConfiguration : IEntityTypeConfiguration<ActivitySchedule>
{
    public void Configure(EntityTypeBuilder<ActivitySchedule> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(s => s.DaysOfWeek).IsRequired().HasMaxLength(40).HasColumnType("VARCHAR(40)");
        builder.HasOne(s => s.Activity).WithMany(a => a.Schedules).HasForeignKey(s => s.ActivityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.CoachUser).WithMany().HasForeignKey(s => s.CoachUserId).OnDelete(DeleteBehavior.SetNull);
        builder.ToTable("activity_schedules");
    }
}

public class ActivitySessionConfiguration : IEntityTypeConfiguration<ActivitySession>
{
    public void Configure(EntityTypeBuilder<ActivitySession> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(s => s.Status).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.HasOne(s => s.Activity).WithMany(a => a.Sessions).HasForeignKey(s => s.ActivityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Schedule).WithMany().HasForeignKey(s => s.ScheduleId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(s => s.CoachUser).WithMany().HasForeignKey(s => s.CoachUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(s => new { s.TenantId, s.StartsAtUtc });

        // Session-generation idempotency: same Schedule + same start instant can only exist once.
        builder.HasIndex(s => new { s.ScheduleId, s.StartsAtUtc })
            .IsUnique()
            .HasFilter("[ScheduleId] IS NOT NULL AND [IsDeleted] = 0");
        builder.ToTable("activity_sessions");
    }
}

public class ActivityBookingConfiguration : IEntityTypeConfiguration<ActivityBooking>
{
    public void Configure(EntityTypeBuilder<ActivityBooking> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(b => b.Status).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(b => b.Source).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(b => b.GuestName).HasMaxLength(200).HasColumnType("NVARCHAR(200)");
        builder.Property(b => b.GuestPhone).HasMaxLength(30).HasColumnType("NVARCHAR(30)");
        builder.HasOne(b => b.Session).WithMany(s => s.Bookings).HasForeignKey(b => b.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.Member).WithMany().HasForeignKey(b => b.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.CoveringMembership).WithMany().HasForeignKey(b => b.CoveringMembershipId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(b => b.Sale).WithMany().HasForeignKey(b => b.SaleId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(b => b.CheckedInByUser).WithMany().HasForeignKey(b => b.CheckedInByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(b => b.Attendance).WithOne(a => a.Booking).HasForeignKey<GymAttendance>(a => a.BookingId).OnDelete(DeleteBehavior.SetNull);

        // Duplicate-booking guard at the database level: one active booking per member/session.
        // Cancelled (either kind) frees the seat; no_show/checked_in/booked keep it held until
        // the session is over. Capacity itself is enforced transactionally (UPDLOCK + recount).
        builder.HasIndex(b => new { b.TenantId, b.SessionId, b.MemberId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [MemberId] IS NOT NULL AND [Status] IN ('booked', 'checked_in', 'no_show')");

        // A drop-in sale pays for exactly one booking. Filter matches the quota-consuming set
        // (booked, checked_in, cancelled_late, no_show) so a timely cancellation frees the paid
        // sale for a new booking, same as it frees a plan's quota credit.
        builder.HasIndex(b => b.SaleId)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [SaleId] IS NOT NULL AND [Status] IN ('booked', 'checked_in', 'cancelled_late', 'no_show')");
        builder.ToTable("activity_bookings");
    }
}

public class PlanEntitlementConfiguration : IEntityTypeConfiguration<PlanEntitlement>
{
    public void Configure(EntityTypeBuilder<PlanEntitlement> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        builder.Property(e => e.AccessMode).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(e => e.QuotaPeriod).HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.HasOne(e => e.Plan).WithMany(p => p.Entitlements).HasForeignKey(e => e.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Activity).WithMany(a => a.PlanEntitlements).HasForeignKey(e => e.ActivityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.PlanId, e.ActivityId });
        builder.ToTable("plan_entitlements");
    }
}
