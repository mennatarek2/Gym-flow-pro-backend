namespace GMS.Platform.Interfaces;

using GMS.Platform.DTOs;

public interface IPlatformCustomerService
{
    Task<List<PlatformCustomerListItemDto>> ListCustomersAsync(string? status, CancellationToken ct = default);
    Task<PlatformCustomerDetailDto?> GetCustomerAsync(Guid id, CancellationToken ct = default);
    Task<PlatformCustomerProfileDto?> GetProfileAsync(Guid id, CancellationToken ct = default);
    Task<PlatformCustomerDetailDto> CreateCustomerAsync(UpsertPlatformCustomerRequest request, Guid actorId, CancellationToken ct = default);
    Task<PlatformCustomerDetailDto?> UpdateCustomerAsync(Guid id, UpsertPlatformCustomerRequest request, Guid actorId, CancellationToken ct = default);
    /// <summary>Ops/Admin only — link or unlink a Cloud TenantId. Ignored on ordinary customer upsert.</summary>
    Task<PlatformCustomerDetailDto?> SetCustomerCloudLinkAsync(Guid id, Guid? tenantId, Guid actorId, CancellationToken ct = default);
    Task<InitiateOwnerPasswordResetResult?> InitiateOwnerPasswordResetAsync(Guid id, string reason, Guid actorId, CancellationToken ct = default);

    Task<List<PlatformCatalogProductDto>> ListProductsAsync(bool includeInactive, CancellationToken ct = default);
    Task<PlatformCatalogProductDto> CreateProductAsync(UpsertCatalogProductRequest request, Guid actorId, CancellationToken ct = default);
    Task<PlatformCatalogProductDto?> UpdateProductAsync(Guid id, UpsertCatalogProductRequest request, Guid actorId, CancellationToken ct = default);

    Task<List<PlatformContractDto>> ListContractsAsync(Guid? customerId, CancellationToken ct = default);
    Task<PlatformContractDto?> GetContractAsync(Guid id, CancellationToken ct = default);
    Task<PlatformContractDto> CreateContractAsync(CreatePlatformContractRequest request, Guid actorId, CancellationToken ct = default);
    Task<PlatformContractDto?> ChangeContractStatusAsync(Guid id, string status, Guid actorId, CancellationToken ct = default);

    Task<List<PlatformCustomerPaymentDto>> ListPaymentsAsync(Guid? customerId, Guid? contractId, CancellationToken ct = default);
    Task<PlatformCustomerPaymentDto> RecordPaymentAsync(RecordCustomerPaymentRequest request, Guid actorId, CancellationToken ct = default);

    Task<List<PlatformSupportTicketDto>> ListTicketsAsync(Guid? customerId, string? status, CancellationToken ct = default);
    Task<PlatformSupportTicketDto?> GetTicketAsync(Guid id, CancellationToken ct = default);
    Task<PlatformSupportTicketDto> CreateTicketAsync(CreateSupportTicketRequest request, Guid actorId, CancellationToken ct = default);
    Task<PlatformSupportTicketDto?> UpdateTicketAsync(Guid id, UpdateSupportTicketRequest request, Guid actorId, CancellationToken ct = default);
}
