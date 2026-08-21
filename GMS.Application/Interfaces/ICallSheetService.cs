namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.CallSheet;

/// <summary>
/// Daily member follow-up queue. Membership / sales / attendance remain owned by those modules.
/// </summary>
public interface ICallSheetService
{
    Task<Result<FollowUpListDto>> GetQueueAsync(
        Guid tenantId, Guid? currentAppUserId, string? date, string? reason, string? priority,
        string? status, string? assignee, string? q);

    Task<Result<FollowUpSummaryDto>> GetSummaryAsync(Guid tenantId);

    Task<Result<FollowUpDetailDto>> GetByIdAsync(Guid followUpId, Guid tenantId);

    Task<Result<FollowUpDto>> CreateAsync(Guid tenantId, Guid staffUserId, CreateFollowUpRequest request);

    Task<Result<bool>> RecordOutcomeAsync(
        Guid followUpId, Guid tenantId, Guid staffUserId, RecordCallOutcomeRequest request);

    Task<Result<bool>> CompleteAsync(Guid followUpId, Guid tenantId, Guid staffUserId, string? note);

    /// <summary>Legacy renewal list. Dashboard still reads this. Cairo dates.</summary>
    Task<Result<List<CallSheetEntryDto>>> GetExpiringAsync(Guid tenantId, int days);

    Task<Result<List<RenewalRateDto>>> GetRenewalRateAsync(
        Guid tenantId, DateOnly from, DateOnly to, Guid? staffUserId);
}
