namespace GMS.Core.Interfaces;

using GMS.Core.Entities;

/// <summary>
/// Domain repository for MemberInvitation operations.
/// </summary>
public interface IInvitationRepository
{
    /// <summary>
    /// Counts guest_pass invitations <b>consumed</b> by a member in a quota period (YYYY-MM).
    /// Only rows with <c>VisitedAtUtc</c> set count — send/pending/expired do not.
    /// </summary>
    Task<int> GetQuotaUsageAsync(Guid memberId, Guid tenantId, string quotaPeriod, CancellationToken ct = default);

    /// <summary>
    /// Creates a new invitation record.
    /// </summary>
    Task<MemberInvitation> CreateAsync(MemberInvitation invitation, CancellationToken ct = default);

    /// <summary>
    /// Marks an invitation as visited (guest arrived).
    /// </summary>
    Task MarkVisitedAsync(Guid invitationId, CancellationToken ct = default);

    /// <summary>
    /// Marks an invitation as converted (guest became a member).
    /// </summary>
    Task MarkConvertedAsync(Guid invitationId, Guid convertedMemberId, CancellationToken ct = default);

    /// <summary>
    /// Gets all invitations sent by a member, ordered by most recent.
    /// </summary>
    Task<List<MemberInvitation>> GetByMemberAsync(Guid memberId, Guid tenantId, CancellationToken ct = default);
}
