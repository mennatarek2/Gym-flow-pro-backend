namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Promo;

/// <summary>
/// Promo code management and server-side discount pricing.
/// </summary>
public interface IPromoService
{
    /// <summary>
    /// Validates a promo code against a plan/member and computes the resulting price.
    /// Never throws for business rejections — those come back as a successful Result whose
    /// Data.IsValid is false and Data.FailureReason names the rule that failed
    /// (see GMS.Core.Constants.PromoValidationReasons).
    /// </summary>
    Task<Result<PromoValidationResult>> ValidateAndPriceAsync(string code, Guid planId, Guid memberId, Guid tenantId);

    /// <summary>
    /// Atomically increments UsesCount if the code hasn't hit MaxUses, via a conditional UPDATE.
    /// Returns true iff a row was actually updated. Must be called inside the caller's sale transaction,
    /// immediately before committing, so a lost race never leaves a sale referencing an over-used code.
    /// </summary>
    Task<bool> TryConsumeAsync(Guid promoCodeId, Guid tenantId);

    Task<Result<PromoCodeDto>> CreateAsync(Guid tenantId, CreatePromoCodeRequest request);

    Task<Result<PromoCodeDto>> UpdateAsync(Guid id, UpdatePromoCodeRequest request);

    Task<Result<PromoCodeDto>> GetByIdAsync(Guid id);

    /// <summary>Soft deactivate: sets IsActive = false. Does not delete or affect UsesCount.</summary>
    Task<Result<bool>> DeactivateAsync(Guid id);

    Task<Result<PagedResult<PromoCodeDto>>> GetPagedAsync(Guid tenantId, bool? activeOnly, bool? validToday, int page, int pageSize);
}
