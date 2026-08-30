namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(n => n.TenantId).IsRequired();
        builder.Property(n => n.MemberId);
        builder.Property(n => n.AppUserId);
        builder.Property(n => n.Channel).HasMaxLength(20).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.TitleAr).HasMaxLength(200).HasDefaultValue(string.Empty);
        builder.Property(n => n.Body).HasMaxLength(2000).IsRequired();
        builder.Property(n => n.BodyAr).HasMaxLength(2000).HasDefaultValue(string.Empty);
        builder.Property(n => n.Status).HasMaxLength(20).HasDefaultValue("pending");
        builder.Property(n => n.ExternalMessageId).HasMaxLength(200);
        builder.Property(n => n.Type).HasMaxLength(80);
        builder.Property(n => n.Category).HasMaxLength(40);
        builder.Property(n => n.Priority).HasMaxLength(20);
        builder.Property(n => n.EntityType).HasMaxLength(60);
        builder.Property(n => n.ActionUrl).HasMaxLength(500);
        builder.Property(n => n.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(n => new { n.TenantId, n.MemberId });
        builder.HasIndex(n => new { n.TenantId, n.AppUserId });
        builder.HasIndex(n => new { n.MemberId, n.ReadAtUtc });
        builder.HasIndex(n => n.SentAtUtc);
        builder.HasIndex(n => new { n.TenantId, n.AppUserId, n.ReadAtUtc });
        builder.HasIndex(n => new { n.TenantId, n.AppUserId, n.CreatedAtUtc });
        builder.HasIndex(n => new { n.TenantId, n.ExternalMessageId });
        builder.HasIndex(n => new { n.TenantId, n.Category });

        builder.HasOne(n => n.Tenant).WithMany()
            .HasForeignKey(n => n.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(n => n.Member).WithMany()
            .HasForeignKey(n => n.MemberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(n => n.AppUser).WithMany()
            .HasForeignKey(n => n.AppUserId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("notifications");
    }
}
