namespace GMS.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Invitation repository with quota counting and status transitions.
/// </summary>
public class InvitationRepository : IInvitationRepository
{
    private readonly GymFlowProDbContext _context;

    public InvitationRepository(GymFlowProDbContext context)
    {
        _context = context;
    }

    public async Task<int> GetQuotaUsageAsync(Guid memberId, Guid tenantId, string quotaPeriod, CancellationToken ct = default)
    {
        // Consume-on-attendance: pending/expired/cancelled sends never spend quota.
        return await _context.MemberInvitations
            .CountAsync(i => i.InvitingMemberId == memberId
                          && i.TenantId == tenantId
                          && i.QuotaPeriod == quotaPeriod
                          && i.InvitationType == "guest_pass"
                          && i.VisitedAtUtc != null, ct);
    }

    public async Task<MemberInvitation> CreateAsync(MemberInvitation invitation, CancellationToken ct = default)
    {
        await _context.MemberInvitations.AddAsync(invitation, ct);
        await _context.SaveChangesAsync(ct);
        return invitation;
    }

    public async Task MarkVisitedAsync(Guid invitationId, CancellationToken ct = default)
    {
        var invitation = await _context.MemberInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId, ct);

        if (invitation != null)
        {
            invitation.Status = "visited";
            invitation.VisitedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task MarkConvertedAsync(Guid invitationId, Guid convertedMemberId, CancellationToken ct = default)
    {
        var invitation = await _context.MemberInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId, ct);

        if (invitation != null)
        {
            invitation.Status = "converted";
            invitation.ConvertedMemberId = convertedMemberId;
            invitation.ConvertedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task<List<MemberInvitation>> GetByMemberAsync(Guid memberId, Guid tenantId, CancellationToken ct = default)
    {
        return await _context.MemberInvitations
            .Where(i => i.InvitingMemberId == memberId && i.TenantId == tenantId)
            .OrderByDescending(i => i.SentAtUtc)
            .ToListAsync(ct);
    }
}
