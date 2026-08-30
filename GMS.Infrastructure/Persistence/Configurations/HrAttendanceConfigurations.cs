namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class EmployeeShiftConfiguration : IEntityTypeConfiguration<EmployeeShift>
{
    public void Configure(EntityTypeBuilder<EmployeeShift> builder)
    {
        builder.ToTable("employee_shifts");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.Name).IsRequired().HasMaxLength(80).HasColumnType("NVARCHAR(80)");
        builder.Property(s => s.StartTime).IsRequired().HasColumnType("TIME");
        builder.Property(s => s.EndTime).IsRequired().HasColumnType("TIME");
        builder.Property(s => s.BreakMinutes).IsRequired().HasDefaultValue(0);
        builder.Property(s => s.GraceMinutes).IsRequired().HasDefaultValue(0);
        builder.Property(s => s.IsActive).IsRequired().HasDefaultValue(true);

        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.UpdatedAtUtc);
        builder.Property(s => s.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(s => new { s.TenantId, s.Name }).IsUnique();

        builder.HasOne(s => s.Tenant).WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class EmployeeScheduleAssignmentConfiguration : IEntityTypeConfiguration<EmployeeScheduleAssignment>
{
    public void Configure(EntityTypeBuilder<EmployeeScheduleAssignment> builder)
    {
        builder.ToTable("employee_schedule_assignments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.EmployeeId).IsRequired();
        builder.Property(a => a.EmployeeShiftId).IsRequired();
        builder.Property(a => a.Date).IsRequired().HasColumnType("DATE");
        builder.Property(a => a.Notes).HasMaxLength(500).HasColumnType("NVARCHAR(500)");

        builder.Property(a => a.CreatedAtUtc).IsRequired();
        builder.Property(a => a.UpdatedAtUtc);
        builder.Property(a => a.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.Date }).IsUnique();
        builder.HasIndex(a => new { a.TenantId, a.Date });

        builder.HasOne(a => a.Employee).WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.EmployeeShift).WithMany(s => s.Assignments).HasForeignKey(a => a.EmployeeShiftId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class EmployeeAttendanceConfiguration : IEntityTypeConfiguration<EmployeeAttendance>
{
    public void Configure(EntityTypeBuilder<EmployeeAttendance> builder)
    {
        builder.ToTable("employee_attendances");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.EmployeeId).IsRequired();
        builder.Property(a => a.AttendanceDate).IsRequired().HasColumnType("DATE");
        builder.Property(a => a.Status).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(a => a.Source).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(a => a.Notes).HasMaxLength(500).HasColumnType("NVARCHAR(500)");

        builder.Property(a => a.CreatedAtUtc).IsRequired();
        builder.Property(a => a.UpdatedAtUtc);
        builder.Property(a => a.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.AttendanceDate }).IsUnique();
        builder.HasIndex(a => new { a.TenantId, a.AttendanceDate });

        builder.HasOne(a => a.Employee).WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Schedule).WithMany(s => s.Attendances).HasForeignKey(a => a.ScheduleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.LeaveRequest).WithMany().HasForeignKey(a => a.LeaveRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}
