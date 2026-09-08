namespace GMS.Infrastructure.Repositories;

using Microsoft.EntityFrameworkCore;
using GMS.Core.Entities;
using GMS.Core.Exceptions;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Attendance repository with today-check and paginated history.
/// </summary>
public class AttendanceRepository : IAttendanceRepository
{
    private const string DuplicateCheckinIndexName = "IX_gym_attendance_TenantId_MemberId_AttendanceDateCairo_Unique";

    private readonly GymFlowProDbContext _context;

    public AttendanceRepository(GymFlowProDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Inserts a gym-floor check-in. Throws <see cref="DuplicateCheckinException"/> instead of
    /// letting a unique-constraint violation surface as a raw 500 — this is the last line of
    /// defense against a genuine race (two concurrent requests both pass the application-level
    /// "already checked in today" query); the DB constraint is what actually decides the winner.
    /// </summary>
    public async Task<GymAttendance> CreateCheckinAsync(GymAttendance attendance, CancellationToken ct = default)
    {
        await _context.GymAttendances.AddAsync(attendance, ct);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateCheckinViolation(ex))
        {
            throw new DuplicateCheckinException(
                "لقد سجلت دخولك اليوم بالفعل / You have already checked in today", ex);
        }
        return attendance;
    }

    private static bool IsDuplicateCheckinViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains(DuplicateCheckinIndexName, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<List<GymAttendance>> GetTodayAsync(Guid tenantId, CancellationToken ct = default)
    {
        var today = MembershipOperational.TodayCairo();
        var (utcStart, utcEndExclusive) = MembershipOperational.CairoInclusiveRangeUtc(today, today);

        return await _context.GymAttendances
            .Where(a => a.TenantId == tenantId
                        && a.CheckInAtUtc >= utcStart
                        && a.CheckInAtUtc < utcEndExclusive)
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
        var today = MembershipOperational.TodayCairo();
        var (utcStart, utcEndExclusive) = MembershipOperational.CairoInclusiveRangeUtc(today, today);

        return await _context.GymAttendances
            .AnyAsync(a => a.MemberId == memberId
                        && a.TenantId == tenantId
                        && a.CheckInAtUtc >= utcStart
                        && a.CheckInAtUtc < utcEndExclusive, ct);
    }
}
