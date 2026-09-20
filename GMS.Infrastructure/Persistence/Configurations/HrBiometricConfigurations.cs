namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class BiometricDeviceConfiguration : IEntityTypeConfiguration<BiometricDevice>
{
    public void Configure(EntityTypeBuilder<BiometricDevice> builder)
    {
        builder.ToTable("biometric_devices");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(d => d.TenantId).IsRequired();
        builder.Property(d => d.DeviceCode).IsRequired().HasMaxLength(40).HasColumnType("NVARCHAR(40)");
        builder.Property(d => d.DisplayName).IsRequired().HasMaxLength(120).HasColumnType("NVARCHAR(120)");
        builder.Property(d => d.Vendor).HasMaxLength(80).HasColumnType("NVARCHAR(80)");
        builder.Property(d => d.Model).HasMaxLength(80).HasColumnType("NVARCHAR(80)");
        builder.Property(d => d.IntegrationType).IsRequired().HasMaxLength(40).HasColumnType("VARCHAR(40)");
        builder.Property(d => d.LocationLabel).HasMaxLength(120).HasColumnType("NVARCHAR(120)");
        builder.Property(d => d.IsEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(d => d.HealthStatus).IsRequired().HasMaxLength(40).HasColumnType("VARCHAR(40)");
        builder.Property(d => d.LastError).HasMaxLength(500).HasColumnType("NVARCHAR(500)");
        builder.Property(d => d.SafeConfigJson).HasMaxLength(4000).HasColumnType("NVARCHAR(4000)");
        builder.Property(d => d.ApiKeyHash).IsRequired().HasMaxLength(64).HasColumnType("VARCHAR(64)");
        builder.Property(d => d.ApiKeyPrefix).IsRequired().HasMaxLength(16).HasColumnType("VARCHAR(16)");

        builder.Property(d => d.CreatedAtUtc).IsRequired();
        builder.Property(d => d.UpdatedAtUtc);
        builder.Property(d => d.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(d => new { d.TenantId, d.DeviceCode }).IsUnique();
        builder.HasIndex(d => new { d.TenantId, d.ApiKeyHash }).IsUnique();
        builder.HasIndex(d => d.TenantId);

        builder.HasOne(d => d.Tenant).WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class BiometricEmployeeMappingConfiguration : IEntityTypeConfiguration<BiometricEmployeeMapping>
{
    public void Configure(EntityTypeBuilder<BiometricEmployeeMapping> builder)
    {
        builder.ToTable("biometric_employee_mappings");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(m => m.TenantId).IsRequired();
        builder.Property(m => m.BiometricDeviceId).IsRequired();
        builder.Property(m => m.EmployeeId).IsRequired();
        builder.Property(m => m.DeviceUserId).IsRequired().HasMaxLength(64).HasColumnType("NVARCHAR(64)");
        builder.Property(m => m.IsEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(m => m.DisabledReason).HasMaxLength(300).HasColumnType("NVARCHAR(300)");
        builder.Property(m => m.RemoteDisableStatus).IsRequired().HasMaxLength(40).HasColumnType("VARCHAR(40)");
        builder.Property(m => m.RemoteDisableNote).HasMaxLength(300).HasColumnType("NVARCHAR(300)");

        builder.Property(m => m.CreatedAtUtc).IsRequired();
        builder.Property(m => m.UpdatedAtUtc);
        builder.Property(m => m.IsDeleted).IsRequired().HasDefaultValue(false);

        // One active device-user per device (enabled + not soft-deleted). Disabled rows may keep history.
        builder.HasIndex(m => new { m.TenantId, m.BiometricDeviceId, m.DeviceUserId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [IsEnabled] = 1");

        // One active mapping per employee per device.
        builder.HasIndex(m => new { m.TenantId, m.BiometricDeviceId, m.EmployeeId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [IsEnabled] = 1");

        builder.HasIndex(m => new { m.TenantId, m.EmployeeId });

        builder.HasOne(m => m.BiometricDevice).WithMany(d => d.Mappings).HasForeignKey(m => m.BiometricDeviceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Employee).WithMany().HasForeignKey(m => m.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class BiometricAttendanceEventConfiguration : IEntityTypeConfiguration<BiometricAttendanceEvent>
{
    public void Configure(EntityTypeBuilder<BiometricAttendanceEvent> builder)
    {
        builder.ToTable("biometric_attendance_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.BiometricDeviceId).IsRequired();
        builder.Property(e => e.VendorEventId).HasMaxLength(120).HasColumnType("NVARCHAR(120)");
        builder.Property(e => e.DeviceUserId).IsRequired().HasMaxLength(64).HasColumnType("NVARCHAR(64)");
        builder.Property(e => e.DeviceTimestampUtc).IsRequired();
        builder.Property(e => e.OriginalDeviceTimeText).HasMaxLength(64).HasColumnType("NVARCHAR(64)");
        builder.Property(e => e.ReceivedAtUtc).IsRequired();
        builder.Property(e => e.PunchDirection).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(e => e.ProcessingStatus).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(e => e.ReviewReason).HasMaxLength(500).HasColumnType("NVARCHAR(500)");
        builder.Property(e => e.ValidationResult).HasMaxLength(80).HasColumnType("VARCHAR(80)");
        builder.Property(e => e.SafePayloadJson).HasMaxLength(4000).HasColumnType("NVARCHAR(4000)");

        builder.Property(e => e.CreatedAtUtc).IsRequired();
        builder.Property(e => e.UpdatedAtUtc);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);

        // Strong idempotency when the vendor supplies an event id.
        builder.HasIndex(e => new { e.TenantId, e.BiometricDeviceId, e.VendorEventId })
            .IsUnique()
            .HasFilter("[VendorEventId] IS NOT NULL AND [IsDeleted] = 0");

        // Fallback dedupe when no vendor event id: same device user + same device timestamp.
        builder.HasIndex(e => new { e.TenantId, e.BiometricDeviceId, e.DeviceUserId, e.DeviceTimestampUtc })
            .IsUnique()
            .HasFilter("[VendorEventId] IS NULL AND [IsDeleted] = 0");

        builder.HasIndex(e => new { e.TenantId, e.ProcessingStatus, e.ReceivedAtUtc });
        builder.HasIndex(e => new { e.TenantId, e.ResolvedEmployeeId, e.DeviceTimestampUtc });

        builder.HasOne(e => e.BiometricDevice).WithMany(d => d.Events).HasForeignKey(e => e.BiometricDeviceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ResolvedEmployee).WithMany().HasForeignKey(e => e.ResolvedEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ResultingAttendance).WithMany().HasForeignKey(e => e.ResultingAttendanceId).OnDelete(DeleteBehavior.Restrict);
    }
}
