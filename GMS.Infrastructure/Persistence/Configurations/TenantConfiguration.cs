namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

/// <summary>
/// Entity Type Configuration for Tenant.
/// Parent entity in the multi-tenant schema.
/// </summary>
public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        // Primary key
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        // Properties
        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(t => t.NameAr)
            .IsRequired()
            .HasMaxLength(150)
            .HasColumnType("NVARCHAR(150)");

        // Unique gym code for tenant resolution via JWT claims
        builder.Property(t => t.GymCode)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(t => t.GymCode)
            .IsUnique();

        builder.Property(t => t.City)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(t => t.Address)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(t => t.PhoneNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(t => t.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(t => t.LogoUrl)
            .HasMaxLength(500);

        builder.Property(t => t.TimeZone)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("Africa/Cairo");

        builder.Property(t => t.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .HasDefaultValue("EGP");

        builder.Property(t => t.MaxMembers)
            .HasDefaultValue(1000);

        builder.Property(t => t.IsActive)
            .HasDefaultValue(true);

        builder.Property(t => t.SubscriptionStartDate)
            .HasColumnType("DATETIME2");

        builder.Property(t => t.SubscriptionEndDate)
            .HasColumnType("DATETIME2");

        builder.Property(t => t.Settings)
            .HasColumnName("Settings")
            .HasColumnType("NVARCHAR(MAX)");

        builder.Property(t => t.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(t => t.UpdatedAtUtc)
            .HasColumnType("DATETIME2");

        builder.Property(t => t.IsDeleted)
            .HasDefaultValue(false);

        // Indexes
        builder.HasIndex(t => t.Email)
            .IsUnique();

        builder.HasIndex(t => t.IsActive);

        // Navigation
        builder.HasMany(t => t.Members)
            .WithOne(m => m.Tenant)
            .HasForeignKey(m => m.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.MembershipPlans)
            .WithOne(mp => mp.Tenant)
            .HasForeignKey(mp => mp.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Memberships)
            .WithOne(m => m.Tenant)
            .HasForeignKey(m => m.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Attendances)
            .WithOne(a => a.Tenant)
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Invitations)
            .WithOne(i => i.Tenant)
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Users)
            .WithOne(u => u.Tenant)
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("tenants", tb =>
            tb.HasCheckConstraint("CK_tenants_Settings_IsJson", "[Settings] IS NULL OR ISJSON([Settings]) = 1"));
    }
}
