namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.AccessCards;

public interface IAccessCardService
{
    Task<Result<AccessCardInventoryDto>> GetInventoryAsync(Guid tenantId);
    Task<Result<PagedResult<AccessCardDto>>> ListAsync(
        Guid tenantId, string? status, string? search, int page, int pageSize);
    Task<Result<AccessCardDto>> GetByIdAsync(Guid tenantId, Guid cardId);
    Task<Result<AccessCardDto?>> GetAssignedForMemberAsync(Guid tenantId, Guid memberId);
    Task<Result<BulkCreateAccessCardsResult>> BulkCreateAsync(
        Guid tenantId, BulkCreateAccessCardsRequest request);
    Task<Result<AccessCardDto>> AssignAsync(Guid tenantId, AssignAccessCardRequest request);
    Task<Result<AccessCardDto>> ReplaceAsync(Guid tenantId, ReplaceAccessCardRequest request);
    Task<Result<AccessCardDto>> MarkLostAsync(Guid tenantId, Guid cardId, MarkAccessCardRequest request);
    Task<Result<AccessCardDto>> MarkDamagedAsync(Guid tenantId, Guid cardId, MarkAccessCardRequest request);
    Task<Result<AccessCardDto>> BlockAsync(Guid tenantId, Guid cardId, MarkAccessCardRequest request);
    Task<Result<AccessCardDto>> UnassignAsync(Guid tenantId, Guid cardId, UnassignAccessCardRequest request);
}
