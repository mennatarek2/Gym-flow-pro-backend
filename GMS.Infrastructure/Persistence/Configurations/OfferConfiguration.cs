namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.ToTable("offers");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(o => o.TenantId).IsRequired();

        builder.Property(o => o.Name).IsRequired().HasMaxLength(150).HasColumnType("NVARCHAR(150)");
        builder.Property(o => o.NameAr).HasMaxLength(150).HasColumnType("NVARCHAR(150)");
        builder.Property(o => o.ShortDescription).IsRequired().HasMaxLength(300).HasColumnType("NVARCHAR(300)");
        builder.Property(o => o.ShortDescriptionAr).HasMaxLength(300).HasColumnType("NVARCHAR(300)");
        builder.Property(o => o.Description).HasMaxLength(2000).HasColumnType("NVARCHAR(2000)");
        builder.Property(o => o.BannerUrl).HasMaxLength(500).HasColumnType("NVARCHAR(500)");

        builder.Property(o => o.StartDate).HasColumnType("DATE").IsRequired();
        builder.Property(o => o.EndDate).HasColumnType("DATE").IsRequired();

        builder.Property(o => o.AppliesTo).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(o => o.PlanIdsJson).HasColumnType("NVARCHAR(MAX)");
        builder.Property(o => o.ProductIdsJson).HasColumnType("NVARCHAR(MAX)");
        builder.Property(o => o.MembershipLabelsJson).HasColumnType("NVARCHAR(MAX)");
        builder.Property(o => o.ProductLabelsJson).HasColumnType("NVARCHAR(MAX)");

        builder.Property(o => o.DiscountType).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(o => o.Value).HasColumnType("DECIMAL(12,2)");
        builder.Property(o => o.MaxDiscount).HasColumnType("DECIMAL(12,2)");
        builder.Property(o => o.MinPurchase).HasColumnType("DECIMAL(12,2)");

        builder.Property(o => o.BuyQty);
        builder.Property(o => o.GetQty);
        builder.Property(o => o.AllMembers).IsRequired().HasDefaultValue(true);
        builder.Property(o => o.NewMembersOnly).IsRequired().HasDefaultValue(false);
        builder.Property(o => o.UsageLimit);
        builder.Property(o => o.PerMemberLimit);
        builder.Property(o => o.UsesCount).IsRequired().HasDefaultValue(0);

        builder.Property(o => o.ShowOnMemberApp).IsRequired().HasDefaultValue(false);
        builder.Property(o => o.Featured).IsRequired().HasDefaultValue(false);
        builder.Property(o => o.ShowBanner).IsRequired().HasDefaultValue(false);
        builder.Property(o => o.DisplayOrder).IsRequired().HasDefaultValue(1);

        builder.Property(o => o.Redemption).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(o => o.PromoCode).HasMaxLength(30).HasColumnType("NVARCHAR(30)");
        builder.Property(o => o.PromoCodeId);

        builder.Property(o => o.IsDraft).IsRequired().HasDefaultValue(false);

        builder.Property(o => o.CreatedAtUtc)
            .HasColumnType("DATETIME2")
            .HasDefaultValueSql("SYSUTCDATETIME()")
            .ValueGeneratedOnAdd();
        builder.Property(o => o.UpdatedAtUtc).HasColumnType("DATETIME2");
        builder.Property(o => o.IsDeleted).HasDefaultValue(false);

        builder.HasIndex(o => new { o.TenantId, o.ShowOnMemberApp, o.StartDate, o.EndDate });
        builder.HasIndex(o => new { o.TenantId, o.CreatedAtUtc });
        builder.HasIndex(o => o.PromoCodeId);

        builder.HasOne(o => o.Tenant).WithMany()
            .HasForeignKey(o => o.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.LinkedPromoCode).WithMany()
            .HasForeignKey(o => o.PromoCodeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
