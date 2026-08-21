namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Invitation;

/// <summary>
/// Invitation product: member submits a friend's contact; staff follows up.
/// JWT <c>sub</c> is Identity user id, not GymMember.Id.
/// </summary>
public interface IInvitationService
{
    Task<Result<SendInvitationResponse>> SendInvitationAsync(
        SendInvitationRequest request, Guid identityUserId, Guid tenantId);

    /// <summary>
    /// Front desk: create an invitation for a GymMember. Same quota rules as the Member App.
    /// <paramref name="gymMemberId"/> is GymMember.Id, not Identity id.
    /// </summary>
    Task<Result<SendInvitationResponse>> SendInvitationForMemberAsync(
        SendInvitationRequest request, Guid gymMemberId, Guid tenantId);

    Task<Result<List<InvitationHistoryResponse>>> GetMemberInvitationsAsync(
        Guid identityUserId, Guid tenantId);

    Task<Result<InvitationQuotaDto>> GetMyInvitationSummaryAsync(
        Guid identityUserId, Guid tenantId);

    Task<Result<InvitationMemberSummaryDto>> GetMemberInvitation360Async(
        Guid gymMemberId, Guid tenantId);

    Task<Result<List<InvitationHistoryResponse>>> GetStaffInvitationsAsync(
        Guid tenantId, string? status, string? query);

    Task<Result<InvitationHistoryResponse>> UpdateInvitationStatusAsync(
        Guid invitationId, Guid tenantId, string status);

    /// <summary>Historical guest_pass hygiene — pending visitDate before Cairo today → expired.</summary>
    Task<int> ExpireOverdueGuestPassesAsync(CancellationToken ct = default);
}
