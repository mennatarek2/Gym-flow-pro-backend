namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Sales;

/// <summary>
/// Atomic point-of-sale processing: member resolution, promo pricing, VAT, split payments,
/// membership creation, and idempotent replay — all inside one database transaction.
/// </summary>
public interface ISaleService
{
    /// <summary>
    /// <paramref name="staffUserId"/> is the JWT "sub" (ApplicationUser.Id) — resolved internally to
    /// app_users.Id, same as CheckinService. <paramref name="callerPermissions"/> is the caller's
    /// "perm" claim set, used only to authorize a manual discount override.
    /// </summary>
    Task<Result<SaleResponse>> CreateSaleAsync(
        CreateSaleRequest request, Guid staffUserId, Guid tenantId, IReadOnlySet<string> callerPermissions);

    /// <summary>Records a payment against an existing sale's outstanding AmountDue.</summary>
    Task<Result<SaleResponse>> RecordPaymentAsync(
        Guid saleId, Guid tenantId, Guid staffUserId, RecordPaymentRequest request);
}
