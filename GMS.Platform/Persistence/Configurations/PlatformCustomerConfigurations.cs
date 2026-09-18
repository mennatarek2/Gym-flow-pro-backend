namespace GMS.Platform.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Platform.Entities;

public class PlatformCustomerConfiguration : IEntityTypeConfiguration<PlatformCustomer>
{
    public void Configure(EntityTypeBuilder<PlatformCustomer> builder)
    {
        builder.ToTable("customers", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_customers_status",
                "[Status] IN ('prospect','active','inactive','churned')");
            t.HasCheckConstraint(
                "CK_customers_contact_method",
                "[PreferredContactMethod] IN ('phone','whatsapp','email')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.BusinessName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.OwnerName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(40);
        builder.Property(x => x.WhatsApp).HasMaxLength(40);
        builder.Property(x => x.Email).HasMaxLength(200);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.PreferredContactMethod).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.LeadSource).HasMaxLength(80);
        builder.Property(x => x.OwnerUsername).HasMaxLength(200);
        builder.Property(x => x.OwnerEmail).HasMaxLength(200);
        builder.Property(x => x.OwnerAccountStatus).HasMaxLength(30).IsRequired();

        builder.HasIndex(x => x.BusinessName);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.AssignedSalesRepPlatformAdminUserId);
        builder.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasFilter("[TenantId] IS NOT NULL")
            .HasDatabaseName("UX_customers_TenantId");
        // Optional FK to dbo.tenants is applied in AddCustomerTenantFk (same database as
        // platform.subscriptions). Keep TenantId nullable so Local-only customers stay valid.

        builder.HasMany(x => x.Contracts)
            .WithOne(x => x.Customer!)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Tickets)
            .WithOne(x => x.Customer!)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PlatformCatalogProductConfiguration : IEntityTypeConfiguration<PlatformCatalogProduct>
{
    public void Configure(EntityTypeBuilder<PlatformCatalogProduct> builder)
    {
        builder.ToTable("catalog_products", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_catalog_products_type",
                "[ProductType] IN ('software','hardware','service','consumable','support')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Sku).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.ProductType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DefaultPrice).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();

        builder.HasIndex(x => x.Sku).IsUnique();
        builder.HasIndex(x => x.IsActive);
    }
}

public class PlatformContractConfiguration : IEntityTypeConfiguration<PlatformContract>
{
    public void Configure(EntityTypeBuilder<PlatformContract> builder)
    {
        builder.ToTable("contracts", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_contracts_status",
                "[Status] IN ('draft','pending','active','completed','expired','cancelled')");
            t.HasCheckConstraint(
                "CK_contracts_payment_status",
                "[PaymentStatus] IN ('unpaid','partial','paid')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ContractNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Discount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Total).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PaymentStatus).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => x.ContractNumber).IsUnique();
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.Status);

        builder.HasMany(x => x.Items)
            .WithOne(x => x.Contract!)
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Payments)
            .WithOne(x => x.Contract!)
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PlatformContractItemConfiguration : IEntityTypeConfiguration<PlatformContractItem>
{
    public void Configure(EntityTypeBuilder<PlatformContractItem> builder)
    {
        builder.ToTable("contract_items", "platform");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SkuSnapshot).HasMaxLength(40).IsRequired();
        builder.Property(x => x.NameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ProductTypeSnapshot).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DescriptionSnapshot).HasMaxLength(1000);
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,2)");
        builder.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
        builder.Property(x => x.DiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.LineTotal).HasColumnType("decimal(18,2)");

        builder.HasIndex(x => x.ContractId);
        builder.HasOne(x => x.CatalogProduct)
            .WithMany()
            .HasForeignKey(x => x.CatalogProductId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class PlatformCustomerPaymentConfiguration : IEntityTypeConfiguration<PlatformCustomerPayment>
{
    public void Configure(EntityTypeBuilder<PlatformCustomerPayment> builder)
    {
        builder.ToTable("customer_payments", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_customer_payments_method",
                "[PaymentMethod] IN ('cash','bank_transfer','other')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.PaymentMethod).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(80);
        builder.Property(x => x.Notes).HasMaxLength(1000);

        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.ContractId);
        builder.HasIndex(x => x.PaymentDate);

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PlatformSupportTicketConfiguration : IEntityTypeConfiguration<PlatformSupportTicket>
{
    public void Configure(EntityTypeBuilder<PlatformSupportTicket> builder)
    {
        builder.ToTable("support_tickets", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_support_tickets_status",
                "[Status] IN ('open','in_progress','waiting_customer','resolved','closed')");
            t.HasCheckConstraint(
                "CK_support_tickets_priority",
                "[Priority] IN ('low','normal','high','critical')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.TicketNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Priority).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Resolution).HasMaxLength(2000);

        builder.HasIndex(x => x.TicketNumber).IsUnique();
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.AssignedToPlatformAdminUserId);
    }
}

public class DeskFeedbackConfiguration : IEntityTypeConfiguration<DeskFeedback>
{
    public void Configure(EntityTypeBuilder<DeskFeedback> builder)
    {
        builder.ToTable("desk_feedback", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_desk_feedback_category",
                "[Category] IN ('feature','problem','general','other')");
            t.HasCheckConstraint(
                "CK_desk_feedback_status",
                "[Status] IN ('new','under_review','planned','resolved','declined')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.GymCode).HasMaxLength(64);
        builder.Property(x => x.GymName).HasMaxLength(200);
        builder.Property(x => x.SenderRole).HasMaxLength(40).IsRequired();
        builder.Property(x => x.SenderEmail).HasMaxLength(200);
        builder.Property(x => x.SenderDisplayName).HasMaxLength(200);
        builder.Property(x => x.Category).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(200);
        builder.Property(x => x.Message).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).IsRequired();
        builder.Property(x => x.AppVersion).HasMaxLength(80);
        builder.Property(x => x.PageUrl).HasMaxLength(500);
        builder.Property(x => x.ClientRequestId).HasMaxLength(64);
        builder.Property(x => x.InternalNote).HasMaxLength(2000);
        builder.Property(x => x.ResponseToCustomer).HasMaxLength(2000);

        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.Category);
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => new { x.TenantId, x.ClientRequestId })
            .IsUnique()
            .HasFilter("[ClientRequestId] IS NOT NULL");

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class PlatformNumberSequenceConfiguration : IEntityTypeConfiguration<PlatformNumberSequence>
{
    public void Configure(EntityTypeBuilder<PlatformNumberSequence> builder)
    {
        builder.ToTable("number_sequences", "platform");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.Kind, x.Year }).IsUnique();
    }
}
