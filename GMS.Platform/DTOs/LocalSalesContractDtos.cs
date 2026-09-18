namespace GMS.Platform.DTOs;

public class LocalSalesContractTermsDto
{
    public string TermsEn { get; set; } = string.Empty;
    public string TermsAr { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

public class UpsertLocalSalesContractTermsRequest
{
    public string TermsEn { get; set; } = string.Empty;
    public string TermsAr { get; set; } = string.Empty;
}

public class IssueLocalSalesContractRequest
{
    public string Language { get; set; } = "en";
}

public class LocalSalesContractDocumentDto
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public Guid CustomerId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public int PrintCount { get; set; }
    public DateTime? LastPrintedAtUtc { get; set; }
}

public class LocalSalesContractHtmlDto : LocalSalesContractDocumentDto
{
    public string Html { get; set; } = string.Empty;
    public bool Issued { get; set; }
}

public class LocalSalesContractSnapshot
{
    public string ContractNumber { get; set; } = string.Empty;
    public string IssuedOn { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string SellerName { get; set; } = "HyMotion";
    public LocalSalesContractCustomerSnap Customer { get; set; } = new();
    public List<LocalSalesContractItemSnap> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string Currency { get; set; } = "EGP";
    public List<LocalSalesContractPaymentSnap> Payments { get; set; } = new();
    public LocalSalesContractLicenseSnap? License { get; set; }
    public string Terms { get; set; } = string.Empty;
}

public class LocalSalesContractCustomerSnap
{
    public string BusinessName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? GymCode { get; set; }
}

public class LocalSalesContractItemSnap
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProductType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class LocalSalesContractPaymentSnap
{
    public string PaymentDate { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public decimal Amount { get; set; }
}

public class LocalSalesContractLicenseSnap
{
    public string Edition { get; set; } = string.Empty;
    public int DeviceLimit { get; set; }
    public string LicenseReference { get; set; } = string.Empty;
    public string? IssuedOn { get; set; }
    public string? GymCode { get; set; }
}
