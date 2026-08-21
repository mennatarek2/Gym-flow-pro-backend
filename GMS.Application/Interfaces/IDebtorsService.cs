namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Debtors;

/// <summary>
/// Front-desk debtors list: members with an outstanding balance across their partially_paid sales,
/// plus a throttled WhatsApp payment-reminder action.
/// </summary>
public interface IDebtorsService
{
    /// <summary>All debtors for the tenant, sorted oldest-due-first (unpaged — used for CSV export).</summary>
    Task<Result<List<DebtorDto>>> GetAllDebtorsAsync(Guid tenantId);

    Task<Result<PagedResult<DebtorDto>>> GetDebtorsPagedAsync(Guid tenantId, int page, int pageSize, Guid? memberId = null);

    Task<Result<DebtorsSummaryDto>> GetSummaryAsync(Guid tenantId);

    /// <summary>Outstanding sales for one member (oldest due first). Empty list + totalDue 0 when nothing is due.</summary>
    Task<Result<MemberOutstandingSalesDto>> GetOutstandingSalesAsync(Guid tenantId, Guid memberId);

    /// <summary>Sends a WhatsApp payment reminder for the member's oldest unpaid sale. Throttled to
    /// once per 48h per (tenant, member) via a Redis-backed key.</summary>
    Task<Result<bool>> RemindAsync(Guid memberId, Guid tenantId, Guid staffUserId);
}
