namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Memberships;

/// <summary>
/// Service interface for membership management.
/// </summary>
public interface IMembershipService
{
    /// <summary>
    /// Get current active membership for a member.
    /// If no active membership, returns the last expired one.
    /// </summary>
    Task<Result<MembershipDto>> GetCurrentMembershipAsync(Guid memberId);

    /// <summary>
    /// Get membership history for a member (paginated).
    /// Orders by EndDate descending (newest first).
    /// </summary>
    Task<Result<PagedResult<MembershipHistoryItemDto>>> GetMembershipHistoryAsync(
        Guid memberId, int page, int pageSize);

    /// <summary>
    /// Assign a new membership to a member.
    /// Validates no active membership exists.
    /// Cash payments require an open shift and create Sale + PaymentTransaction + cash movement.
    /// Gateway payments create a pending membership.
    /// </summary>
    /// <param name="staffUserId">JWT sub (ApplicationUser.Id).</param>
    Task<Result<MembershipDto>> AssignMembershipAsync(
        Guid tenantId, Guid memberId, AssignMembershipRequest request, Guid staffUserId);

    /// <summary>
    /// Renew member's current/expired membership.
    /// Expired gaps start from today; still-active renewals extend from the prior EndDate.
    /// Cash payments require an open shift and register revenue on that shift.
    /// </summary>
    /// <param name="staffUserId">JWT sub (ApplicationUser.Id).</param>
    Task<Result<MembershipDto>> RenewMembershipAsync(
        Guid tenantId, Guid memberId, RenewMembershipRequest request, Guid staffUserId);

    /// <summary>
    /// Stop the operational current plan (active / frozen / scheduled / pending).
    /// Not a refund. Remaining on that membership sale is no longer collected.
    /// </summary>
    Task<Result<MembershipDto>> CancelMembershipAsync(
        Guid tenantId, Guid memberId, Guid staffUserId);
}
