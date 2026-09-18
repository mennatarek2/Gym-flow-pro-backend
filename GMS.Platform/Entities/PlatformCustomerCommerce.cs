namespace GMS.Platform.Entities;

/// <summary>
/// The gym/business the platform sells to. Not a LocalLicense and not a SaaS Tenant —
/// those may be linked later. Passwords, signing keys, and tenant secrets never live here.
/// </summary>
public class PlatformCustomer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string BusinessName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string PreferredContactMethod { get; set; } = "whatsapp";
    public string? Notes { get; set; }
    public string Status { get; set; } = "prospect";

    /// <summary>How the gym reached us (Instagram, Sales Team, Web Form). Not CreatedBy.</summary>
    public string? LeadSource { get; set; }

    public Guid? CreatedByPlatformAdminUserId { get; set; }
    public Guid? AssignedSalesRepPlatformAdminUserId { get; set; }

    /// <summary>Optional SaaS tenant this customer also uses. Local-only customers leave this null.</summary>
    public Guid? TenantId { get; set; }

    public string? OwnerUsername { get; set; }
    public string? OwnerEmail { get; set; }
    public string OwnerAccountStatus { get; set; } = "unknown";
    public DateTime? OwnerAccountCreatedAtUtc { get; set; }
    public DateTime? OwnerLastLoginAtUtc { get; set; }
    public DateTime? PasswordResetInitiatedAtUtc { get; set; }
    public Guid? PasswordResetInitiatedByPlatformAdminUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<PlatformContract> Contracts { get; set; } = new List<PlatformContract>();
    public ICollection<PlatformSupportTicket> Tickets { get; set; } = new List<PlatformSupportTicket>();
}

public class PlatformCatalogProduct
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ProductType { get; set; } = "service";
    public decimal DefaultPrice { get; set; }
    public string Currency { get; set; } = "EGP";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// What the customer agreed to buy. Not a SaaS <see cref="PlatformInvoice"/> and not a payment.
/// </summary>
public class PlatformContract
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public DateOnly ContractDate { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Status { get; set; } = "draft";
    public string Currency { get; set; } = "EGP";
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = "unpaid";
    public string? Notes { get; set; }
    public Guid CreatedByPlatformAdminUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public PlatformCustomer? Customer { get; set; }
    public ICollection<PlatformContractItem> Items { get; set; } = new List<PlatformContractItem>();
    public ICollection<PlatformCustomerPayment> Payments { get; set; } = new List<PlatformCustomerPayment>();
}

public class PlatformContractItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContractId { get; set; }
    public Guid? CatalogProductId { get; set; }
    public string SkuSnapshot { get; set; } = string.Empty;
    public string NameSnapshot { get; set; } = string.Empty;
    public string ProductTypeSnapshot { get; set; } = string.Empty;
    public string? DescriptionSnapshot { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
    public int SortOrder { get; set; }

    public PlatformContract? Contract { get; set; }
    public PlatformCatalogProduct? CatalogProduct { get; set; }
}

/// <summary>
/// Money actually received against a contract. Distinct from SaaS <see cref="PlatformPaymentEvent"/>.
/// Amounts are recorded, not gateway-verified.
/// </summary>
public class PlatformCustomerPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Guid ContractId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public DateOnly PaymentDate { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public Guid RecordedByPlatformAdminUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public PlatformCustomer? Customer { get; set; }
    public PlatformContract? Contract { get; set; }
}

public class PlatformSupportTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TicketNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Guid? ContractId { get; set; }
    public Guid? LocalLicenseId { get; set; }
    public Guid? LocalInstallationId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = "normal";
    public string Status { get; set; } = "open";
    public Guid? AssignedToPlatformAdminUserId { get; set; }
    public Guid CreatedByPlatformAdminUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
    public string? Resolution { get; set; }

    public PlatformCustomer? Customer { get; set; }
}

public class PlatformNumberSequence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = string.Empty;
    public int Year { get; set; }
    public int LastNumber { get; set; }
}
