namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class MemberOrderConfiguration : IEntityTypeConfiguration<MemberOrder>
{
    public void Configure(EntityTypeBuilder<MemberOrder> builder)
    {
        builder.ToTable("member_orders");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.MemberId).IsRequired();
        builder.Property(x => x.WarehouseId).IsRequired();

        builder.Property(x => x.OrderNumber)
            .IsRequired()
            .HasMaxLength(32)
            .HasColumnType("VARCHAR(32)");

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnType("VARCHAR(20)");

        builder.Property(x => x.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .HasColumnType("CHAR(3)")
            .HasDefaultValue("EGP");

        builder.Property(x => x.Subtotal).IsRequired().HasColumnType("DECIMAL(12,2)");
        builder.Property(x => x.Total).IsRequired().HasColumnType("DECIMAL(12,2)");

        builder.Property(x => x.MemberNotes).HasMaxLength(500).HasColumnType("NVARCHAR(500)");
        builder.Property(x => x.RejectionReason).HasMaxLength(500).HasColumnType("NVARCHAR(500)");

        builder.Property(x => x.AcceptedAtUtc);
        builder.Property(x => x.ReadyAtUtc);
        builder.Property(x => x.CompletedAtUtc);
        builder.Property(x => x.RejectedAtUtc);
        builder.Property(x => x.AcceptedByUserId);
        builder.Property(x => x.ReadyByUserId);
        builder.Property(x => x.CompletedByUserId);
        builder.Property(x => x.RejectedByUserId);

        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc);
        builder.Property(x => x.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(x => new { x.TenantId, x.OrderNumber })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.MemberId, x.CreatedAtUtc });

        builder.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Member)
            .WithMany()
            .HasForeignKey(x => x.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AcceptedByUser)
            .WithMany()
            .HasForeignKey(x => x.AcceptedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReadyByUser)
            .WithMany()
            .HasForeignKey(x => x.ReadyByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CompletedByUser)
            .WithMany()
            .HasForeignKey(x => x.CompletedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RejectedByUser)
            .WithMany()
            .HasForeignKey(x => x.RejectedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne(l => l.MemberOrder)
            .HasForeignKey(l => l.MemberOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
