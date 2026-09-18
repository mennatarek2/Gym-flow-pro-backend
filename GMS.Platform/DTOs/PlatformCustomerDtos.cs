namespace GMS.Platform.DTOs;

public class PlatformCustomerListItemDto
{
    public Guid Id { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? LeadSource { get; set; }
    public Guid? AssignedSalesRepPlatformAdminUserId { get; set; }
    public Guid? TenantId { get; set; }
    public int OpenTicketCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class PlatformCustomerDetailDto : PlatformCustomerListItemDto
{
    public string? WhatsApp { get; set; }
    public string? Address { get; set; }
    public string PreferredContactMethod { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid? CreatedByPlatformAdminUserId { get; set; }
    public string? OwnerUsername { get; set; }
    public string? OwnerEmail { get; set; }
    public string OwnerAccountStatus { get; set; } = string.Empty;
    public DateTime? OwnerAccountCreatedAtUtc { get; set; }
    public DateTime? OwnerLastLoginAtUtc { get; set; }
    public DateTime? PasswordResetInitiatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class UpsertPlatformCustomerRequest
{
    public string BusinessName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? PreferredContactMethod { get; set; }
    public string? Notes { get; set; }
    public string? Status { get; set; }
    public string? LeadSource { get; set; }
    public Guid? AssignedSalesRepPlatformAdminUserId { get; set; }
    public Guid? TenantId { get; set; }
    public string? OwnerUsername { get; set; }
    public string? OwnerEmail { get; set; }
}

public class SetCustomerCloudLinkRequest
{
    public Guid? TenantId { get; set; }
}

public class PlatformCatalogProductDto
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ProductType { get; set; } = string.Empty;
    public decimal DefaultPrice { get; set; }
    public string Currency { get; set; } = "EGP";
    public bool IsActive { get; set; }
}

public class UpsertCatalogProductRequest
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ProductType { get; set; } = "service";
    public decimal DefaultPrice { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ContractItemInput
{
    public Guid? CatalogProductId { get; set; }
    public string? Name { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal? UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
}

public class CreatePlatformContractRequest
{
    public Guid CustomerId { get; set; }
    public DateOnly? ContractDate { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal Discount { get; set; }
    public string? Notes { get; set; }
    public List<ContractItemInput> Items { get; set; } = new();
}

public class PlatformContractItemDto
{
    public Guid Id { get; set; }
    public Guid? CatalogProductId { get; set; }
    public string SkuSnapshot { get; set; } = string.Empty;
    public string NameSnapshot { get; set; } = string.Empty;
    public string ProductTypeSnapshot { get; set; } = string.Empty;
    public string? DescriptionSnapshot { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class PlatformContractDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public DateOnly ContractDate { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = "EGP";
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<PlatformContractItemDto> Items { get; set; } = new();
}

public class ChangeContractStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

public class RecordCustomerPaymentRequest
{
    public Guid ContractId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly? PaymentDate { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public class PlatformCustomerPaymentDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public DateOnly PaymentDate { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class CreateSupportTicketRequest
{
    public Guid CustomerId { get; set; }
    public Guid? ContractId { get; set; }
    public Guid? LocalLicenseId { get; set; }
    public Guid? LocalInstallationId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Priority { get; set; }
}

public class UpdateSupportTicketRequest
{
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public Guid? AssignedToPlatformAdminUserId { get; set; }
    public string? Resolution { get; set; }
}

public class PlatformSupportTicketDto
{
    public Guid Id { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public Guid? ContractId { get; set; }
    public Guid? LocalLicenseId { get; set; }
    public Guid? LocalInstallationId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? AssignedToPlatformAdminUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? Resolution { get; set; }
}

public class InitiateOwnerPasswordResetRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class InitiateOwnerPasswordResetResult
{
    public bool Initiated { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class PlatformCustomerProfileDto
{
    public PlatformCustomerDetailDto Customer { get; set; } = new();
    public PlatformContractDto? LatestContract { get; set; }
    public List<PlatformContractItemDto> PurchasedItems { get; set; } = new();
    public decimal ContractTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public string PaymentStatus { get; set; } = "unpaid";
    public LocalLicenseListItemDto? License { get; set; }
    public List<LocalLicenseListItemDto> Licenses { get; set; } = new();
    public LocalInstallationDto? Installation { get; set; }
    public int OpenSupportTicketCount { get; set; }
    public string? LastValidation { get; set; }
}
