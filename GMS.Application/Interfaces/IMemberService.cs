namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Members;

/// <summary>
/// Member management service.
/// </summary>
public interface IMemberService
{
    Task<Result<PagedResult<MemberListItemDto>>> GetMembersAsync(
        Guid tenantId, string? search, string? status, int page, int pageSize);

    Task<Result<MemberDetailDto>> GetMemberByIdAsync(Guid id);

    Task<Result<MemberDetailDto>> CreateMemberAsync(Guid tenantId, CreateMemberRequest request);

    Task<Result<MemberDetailDto>> UpdateMemberAsync(Guid id, UpdateMemberRequest request);

    Task<Result<string>> DeactivateMemberAsync(Guid id);

    /// <summary>
    /// Re-enable a deactivated member account (IsActive = true).
    /// Does not create or change memberships — account flag only.
    /// </summary>
    Task<Result<string>> ReactivateMemberAsync(Guid id);

    Task<Result<PagedResult<AttendanceSummaryDto>>> GetMemberAttendanceAsync(
        Guid memberId, int page, int pageSize);

    Task<Result<MembershipSummaryDto>> GetCurrentMembershipAsync(Guid memberId);

    Task<Result<string>> FreezeMembershipAsync(Guid memberId, DateTime frozenUntil, string? reason);

    Task<Result<string>> UnfreezeMembershipAsync(Guid memberId);
}
