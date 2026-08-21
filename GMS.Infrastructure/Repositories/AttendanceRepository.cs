namespace GMS.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Attendance repository with today-check and paginated history.
/// </summary>
public class AttendanceRepository : IAttendanceRepository
{
    private readonly GymFlowProDbContext _context;

    public AttendanceRepository(GymFlowProDbContext context)
    {
        _context = context;
    }

    public async Task<GymAttendance> CreateCheckinAsync(GymAttendance attendance, CancellationToken ct = default)
    {
        await _context.GymAttendances.AddAsync(attendance, ct);
        await _context.SaveChangesAsync(ct);
        return attendance;
    }

    public async Task<List<GymAttendance>> GetTodayAsync(Guid tenantId, CancellationToken ct = default)
    {
        var todayUtc = DateTime.UtcNow.Date;

        return await _context.GymAttendances
            .Where(a => a.TenantId == tenantId && a.CheckInAtUtc >= todayUtc)
            .Include(a => a.Member)
            .Include(a => a.Membership)
                .ThenInclude(ms => ms!.Plan)
            .OrderByDescending(a => a.CheckInAtUtc)
            .ToListAsync(ct);
    }

    public async Task<List<GymAttendance>> GetMemberHistoryAsync(Guid memberId, Guid tenantId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        return await _context.GymAttendances
            .Where(a => a.MemberId == memberId && a.TenantId == tenantId)
            .Include(a => a.Membership)
                .ThenInclude(ms => ms!.Plan)
            .OrderByDescending(a => a.CheckInAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<bool> HasCheckedInTodayAsync(Guid memberId, Guid tenantId, CancellationToken ct = default)
    {
        var todayUtc = DateTime.UtcNow.Date;

        return await _context.GymAttendances
            .AnyAsync(a => a.MemberId == memberId
                        && a.TenantId == tenantId
                        && a.CheckInAtUtc >= todayUtc, ct);
    }
}
