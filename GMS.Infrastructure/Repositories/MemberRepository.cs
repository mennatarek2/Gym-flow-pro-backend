namespace GMS.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Member repository with optimized queries for check-in, search, and CRUD flows.
/// </summary>
public class MemberRepository : IMemberRepository
{
    private readonly GymFlowProDbContext _context;

    public MemberRepository(GymFlowProDbContext context)
    {
        _context = context;
    }

    public async Task<GymMember?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.GymMembers
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<GymMember?> GetByIdWithMembershipAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.GymMembers
            .Include(m => m.Memberships.Where(ms => ms.Status == "active" || ms.Status == "frozen"))
                .ThenInclude(ms => ms.Plan)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<GymMember?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.GymMembers
            .Include(m => m.Memberships.OrderByDescending(ms => ms.EndDate))
                .ThenInclude(ms => ms.Plan)
            .Include(m => m.Attendances.OrderByDescending(a => a.CheckInAtUtc).Take(5))
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<GymMember?> GetByMemberNumberAsync(string memberNumber, Guid tenantId, CancellationToken ct = default)
    {
        return await _context.GymMembers
            .FirstOrDefaultAsync(m => m.MemberNumber == memberNumber && m.TenantId == tenantId, ct);
    }

    public async Task<List<GymMember>> SearchAsync(string query, Guid tenantId, bool includeInactive = false, CancellationToken ct = default)
    {
        var q = _context.GymMembers
            .Where(m => m.TenantId == tenantId);

        if (!includeInactive)
            q = q.Where(m => m.IsActive);

        var s = query.Trim().ToLower();
        q = q.Where(m =>
            (m.FullName != null && m.FullName.ToLower().Contains(s)) ||
            (m.FullNameAr != null && m.FullNameAr.ToLower().Contains(s)) ||
            (m.PhoneNumber != null && m.PhoneNumber.Contains(query.Trim())) ||
            (m.MemberNumber != null && m.MemberNumber.ToLower().Contains(s)));

        return await q
            .Include(m => m.Memberships.Where(ms =>
                ms.Status == "active" || ms.Status == "frozen" || ms.Status == "expired"
                || ms.Status == "pending" || ms.Status == "cancelled"))
                .ThenInclude(ms => ms.Plan)
            .OrderBy(m => m.FullName)
            .Take(20)
            .ToListAsync(ct);
    }

    public async Task<GymMember?> GetByPhoneAsync(string phoneNumber, Guid tenantId, CancellationToken ct = default)
    {
        return await _context.GymMembers
            .FirstOrDefaultAsync(m => m.PhoneNumber == phoneNumber && m.TenantId == tenantId, ct);
    }

    public async Task<(List<GymMember> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId, string? search, string? status,
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = _context.GymMembers
            .Where(m => m.TenantId == tenantId);

        // Cairo business day — filters must match door eligibility, not raw Status alone.
        var today = MembershipOperational.TodayCairo();

        // Search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(m =>
                (m.FullName != null && m.FullName.ToLower().Contains(s)) ||
                (m.FullNameAr != null && m.FullNameAr.ToLower().Contains(s)) ||
                (m.PhoneNumber != null && m.PhoneNumber.Contains(search.Trim())) ||
                (m.MemberNumber != null && m.MemberNumber.ToLower().Contains(s)));
        }

        // Status filter — use membership Id sets (reliable EF translation vs nested DateOnly Any).
        if (!string.IsNullOrWhiteSpace(status))
        {
            switch (status.ToLowerInvariant())
            {
                case "active":
                {
                    var liveIds = _context.Memberships
                        .Where(ms => ms.Status == "active"
                                  && ms.StartDate <= today
                                  && ms.EndDate >= today)
                        .Select(ms => ms.MemberId);
                    query = query.Where(m => m.IsActive && liveIds.Contains(m.Id));
                    break;
                }
                case "expired":
                {
                    // Member has no currently usable plan, but has an expired / past-end membership.
                    var liveIds = _context.Memberships
                        .Where(ms =>
                            (ms.Status == "active" && ms.StartDate <= today && ms.EndDate >= today)
                            || (ms.Status == "frozen" && ms.EndDate >= today))
                        .Select(ms => ms.MemberId);
                    var expiredIds = _context.Memberships
                        .Where(ms =>
                            ms.Status == "expired"
                            || ((ms.Status == "active" || ms.Status == "frozen") && ms.EndDate < today))
                        .Select(ms => ms.MemberId);
                    query = query.Where(m => m.IsActive
                        && expiredIds.Contains(m.Id)
                        && !liveIds.Contains(m.Id));
                    break;
                }
                case "frozen":
                {
                    var frozenIds = _context.Memberships
                        .Where(ms => ms.Status == "frozen" && ms.EndDate >= today)
                        .Select(ms => ms.MemberId);
                    var liveActiveIds = _context.Memberships
                        .Where(ms => ms.Status == "active"
                                  && ms.StartDate <= today
                                  && ms.EndDate >= today)
                        .Select(ms => ms.MemberId);
                    query = query.Where(m => m.IsActive
                        && frozenIds.Contains(m.Id)
                        && !liveActiveIds.Contains(m.Id));
                    break;
                }
                case "cancelled":
                {
                    var cancelledIds = _context.Memberships
                        .Where(ms => ms.Status == "cancelled")
                        .Select(ms => ms.MemberId);
                    var liveIds = _context.Memberships
                        .Where(ms =>
                            (ms.Status == "active" && ms.StartDate <= today && ms.EndDate >= today)
                            || (ms.Status == "frozen" && ms.EndDate >= today))
                        .Select(ms => ms.MemberId);
                    query = query.Where(m => m.IsActive
                        && cancelledIds.Contains(m.Id)
                        && !liveIds.Contains(m.Id));
                    break;
                }
                case "inactive":
                    query = query.Where(m => !m.IsActive);
                    break;
            }
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Include(m => m.Memberships.Where(ms =>
                ms.Status == "active" || ms.Status == "frozen"
                || ms.Status == "expired" || ms.Status == "pending"
                || ms.Status == "cancelled"))
                .ThenInclude(ms => ms.Plan)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<int> GetNextMemberSequenceAsync(Guid tenantId, CancellationToken ct = default)
    {
        var count = await _context.GymMembers
            .IgnoreQueryFilters()
            .CountAsync(m => m.TenantId == tenantId, ct);

        return count + 1;
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var member = await _context.GymMembers.FindAsync(new object[] { id }, ct);
        if (member != null)
        {
            member.IsActive = false;
            member.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task ReactivateAsync(Guid id, CancellationToken ct = default)
    {
        var member = await _context.GymMembers.FindAsync(new object[] { id }, ct);
        if (member != null)
        {
            member.IsActive = true;
            member.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task AddAsync(GymMember member, CancellationToken ct = default)
    {
        await _context.GymMembers.AddAsync(member, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(GymMember member, CancellationToken ct = default)
    {
        _context.GymMembers.Update(member);
        await _context.SaveChangesAsync(ct);
    }
}
