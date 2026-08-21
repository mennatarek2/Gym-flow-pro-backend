namespace GMS.Platform.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Platform.Entities;

public class PlatformAdminUserConfiguration : IEntityTypeConfiguration<PlatformAdminUser>
{
    public void Configure(EntityTypeBuilder<PlatformAdminUser> builder)
    {
        builder.ToTable("platform_admin_users", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_platform_admin_users_role",
                "[Role] IN ('platform_support','platform_ops','platform_admin')");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Role).HasMaxLength(32).IsRequired();
        builder.Property(x => x.MfaSecret).HasMaxLength(512);

        builder.HasIndex(x => x.Email).IsUnique();
    }
}

public class PlatformAuditLogConfiguration : IEntityTypeConfiguration<PlatformAuditLog>
{
    public void Configure(EntityTypeBuilder<PlatformAuditLog> builder)
    {
        builder.ToTable("platform_audit_log", "platform");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BeforeJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.AfterJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.IpAddress).HasMaxLength(64);

        builder.HasIndex(x => x.ActorPlatformUserId);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}
