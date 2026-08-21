namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

/// <summary>
/// Entity Type Configuration for AppUser.
/// Represents staff members and admins in the system.
/// Can be linked to a GymMember for staff members who are also members.
/// </summary>
public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        // Primary key
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        // Tenant context
        builder.Property(u => u.TenantId)
            .IsRequired();

        builder.HasIndex(u => u.TenantId)
            .IsUnique(false);

        // User information
        builder.Property(u => u.UserId)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(u => new { u.TenantId, u.UserId })
            .IsUnique();

        builder.Property(u => u.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(u => u.Email);

        builder.Property(u => u.PhoneNumber)
            .HasMaxLength(20);

        // Profile
        builder.Property(u => u.ProfilePhotoUrl)
            .HasMaxLength(500);

        // Roles & Permissions
        builder.Property(u => u.Role)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("staff");

        builder.Property(u => u.IsActive)
            .HasDefaultValue(true);

        builder.HasIndex(u => new { u.TenantId, u.IsActive });

        // Access Control
        builder.Property(u => u.LastLoginAtUtc)
            .HasColumnType("DATETIME2");

        builder.Property(u => u.TwoFactorEnabled)
            .HasDefaultValue(false);

        builder.Property(u => u.StaffNumber)
            .HasMaxLength(12)
            .HasColumnType("VARCHAR(12)");

        builder.HasIndex(u => new { u.TenantId, u.StaffNumber })
            .IsUnique()
            .HasFilter("[StaffNumber] IS NOT NULL");

        builder.Property(u => u.JobTitle)
            .HasMaxLength(80);

        builder.Property(u => u.Department)
            .HasMaxLength(40);

        builder.HasIndex(u => new { u.TenantId, u.Department });

        builder.Property(u => u.HireDate)
            .HasColumnType("DATE");

        builder.Property(u => u.Notes)
            .HasMaxLength(2000);

        // Timestamps
        builder.Property(u => u.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(u => u.UpdatedAtUtc)
            .HasColumnType("DATETIME2");

        builder.Property(u => u.IsDeleted)
            .HasDefaultValue(false);

        // Navigation
        builder.HasOne(u => u.Tenant)
            .WithMany(t => t.Users)
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("app_users");
    }
}
