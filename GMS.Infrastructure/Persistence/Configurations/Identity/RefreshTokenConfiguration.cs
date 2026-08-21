namespace GMS.Infrastructure.Persistence.Configurations.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities.Identity;

/// <summary>
/// Entity Type Configuration for RefreshToken.
/// Stores hashed refresh tokens with rotation tracking.
/// </summary>
public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        // Primary key
        builder.HasKey(rt => rt.Id);
        builder.Property(rt => rt.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        // Token hash (indexed for fast lookup)
        builder.Property(rt => rt.TokenHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(rt => rt.TokenHash)
            .IsUnique();

        // Foreign key to ApplicationUser
        builder.Property(rt => rt.UserId)
            .IsRequired();

        // Tenant context
        builder.Property(rt => rt.TenantId)
            .IsRequired();

        builder.HasIndex(rt => rt.TenantId);
        builder.HasIndex(rt => new { rt.UserId, rt.TenantId });

        // Expiration
        builder.Property(rt => rt.ExpiresAtUtc)
            .HasColumnType("DATETIME2")
            .IsRequired();

        // Revocation
        builder.Property(rt => rt.RevokedAtUtc)
            .HasColumnType("DATETIME2");

        // Rotation chain
        builder.Property(rt => rt.ReplacedByTokenHash)
            .HasMaxLength(128);

        // IP tracking
        builder.Property(rt => rt.CreatedByIp)
            .HasMaxLength(50);

        // Timestamps
        builder.Property(rt => rt.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();

        builder.Property(rt => rt.UpdatedAtUtc)
            .HasColumnType("DATETIME2");

        builder.Property(rt => rt.IsDeleted)
            .HasDefaultValue(false);

        // Computed properties are NOT mapped
        builder.Ignore(rt => rt.IsExpired);
        builder.Ignore(rt => rt.IsRevoked);
        builder.Ignore(rt => rt.IsActive);

        // Navigation
        builder.HasOne(rt => rt.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable("refresh_tokens");
    }
}
