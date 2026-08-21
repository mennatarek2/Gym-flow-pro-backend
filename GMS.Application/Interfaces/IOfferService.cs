namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Offers;

public interface IOfferService
{
    Task<Result<List<OfferDto>>> ListStaffAsync(Guid tenantId, CancellationToken ct = default);
    Task<Result<OfferDto>> GetStaffByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<Result<OfferDto>> CreateAsync(Guid tenantId, UpsertOfferRequest request, CancellationToken ct = default);
    Task<Result<OfferDto>> UpdateAsync(Guid tenantId, Guid id, UpsertOfferRequest request, CancellationToken ct = default);
    Task<Result<OfferDto>> EndAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<Result<List<MemberOfferDto>>> ListMemberAsync(Guid tenantId, Guid identityUserId, CancellationToken ct = default);
    Task<Result<MemberOfferDto>> GetMemberByIdAsync(Guid tenantId, Guid identityUserId, Guid id, CancellationToken ct = default);
}
