namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

/// <summary>
/// Entity Type Configuration for AuditEvent.
/// Audit rows are append-only: never updated or hard/soft deleted.
/// </summary>
public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(a => a.TenantId)
            .IsRequired();

        builder.Property(a => a.ActorUserId);

        builder.Property(a => a.Action)
            .IsRequired()
            .HasMaxLength(60)
            .HasColumnType("VARCHAR(60)");

        builder.Property(a => a.EntityType)
            .HasMaxLength(40)
            .HasColumnType("VARCHAR(40)");

        builder.Property(a => a.EntityId);

        builder.Property(a => a.BeforeJson)
            .HasColumnType("NVARCHAR(MAX)");

        builder.Property(a => a.AfterJson)
            .HasColumnType("NVARCHAR(MAX)");

        builder.Property(a => a.IpAddress)
            .HasMaxLength(45)
            .HasColumnType("NVARCHAR(45)");

        builder.Property(a => a.ImpersonatedByPlatformUserId);

        builder.Property(a => a.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(a => a.UpdatedAtUtc)
            .HasColumnType("DATETIME2");

        builder.Property(a => a.IsDeleted)
            .HasDefaultValue(false);

        builder.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
        builder.HasIndex(a => new { a.TenantId, a.CreatedAtUtc })
            .IsDescending(false, true);
        builder.HasIndex(a => new { a.TenantId, a.ActorUserId, a.CreatedAtUtc });

        builder.HasOne(a => a.Tenant)
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // ActorUserId -> app_users.Id (domain PK), NOT AspNetUsers.Id — see AuditEvent.ActorUserId doc comment.
        builder.HasOne(a => a.ActorUser)
            .WithMany()
            .HasForeignKey(a => a.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ToTable("audit_events");
    }
}
