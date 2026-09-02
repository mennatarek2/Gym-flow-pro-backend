namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Activities;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Read-only Member App classes: upcoming sessions with real schedule, trainer, price, and capacity.
/// No bookings or payments are created by these methods.
/// </summary>
public class MemberClassService : IMemberClassService
{
    private static readonly TimeZoneInfo CairoTz =
        TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly GymFlowProDbContext _db;
    private readonly ISessionGenerationService _sessionGeneration;
    private readonly ILogger<MemberClassService> _logger;

    public MemberClassService(
        GymFlowProDbContext db,
        ISessionGenerationService sessionGeneration,
        ILogger<MemberClassService> logger)
    {
        _db = db;
        _sessionGeneration = sessionGeneration;
        _logger = logger;
    }

    public async Task<Result<List<MemberClassListItemDto>>> ListUpcomingAsync(
        Guid tenantId,
        Guid identityUserId,
        Guid? activityId = null,
        DateTime? fromUtc = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        var memberError = await EnsureMemberProfileAsync(tenantId, identityUserId, ct);
        if (memberError != null)
            return Result<List<MemberClassListItemDto>>.Failure(memberError);

        await TryGenerateSessionsAsync(tenantId, ct);

        var now = DateTime.UtcNow;
        var from = fromUtc.HasValue && fromUtc.Value > now ? fromUtc.Value : now;
        var take = Math.Clamp(limit, 1, 200);

        var query = _db.ActivitySessions.AsNoTracking()
            .Include(s => s.Activity)
            .Include(s => s.CoachUser)
            .Where(s => s.TenantId == tenantId
                        && !s.IsDeleted
                        && s.StartsAtUtc >= from
                        && s.Status != ActivitySessionStatuses.Cancelled
                        && s.Activity != null
                        && s.Activity.Kind == ActivityKinds.Class
                        && s.Activity.IsActive
                        && !s.Activity.IsDeleted
                        && s.Activity.VisibleToMembers);

        if (activityId.HasValue)
            query = query.Where(s => s.ActivityId == activityId.Value);

        var sessions = await query
            .OrderBy(s => s.StartsAtUtc)
            .Take(take)
            .ToListAsync(ct);

        var bookedCounts = await LoadSeatOccupyingCountsAsync(tenantId, sessions.Select(s => s.Id), ct);

        var dtos = sessions.Select(s => ToListItemDto(s, bookedCounts.GetValueOrDefault(s.Id, 0))).ToList();
        return Result<List<MemberClassListItemDto>>.Success(dtos);
    }

    public async Task<Result<MemberClassDetailsDto>> GetByIdAsync(
        Guid tenantId,
        Guid identityUserId,
        Guid sessionId,
        CancellationToken ct = default)
    {
        var memberError = await EnsureMemberProfileAsync(tenantId, identityUserId, ct);
        if (memberError != null)
            return Result<MemberClassDetailsDto>.Failure(memberError);

        await TryGenerateSessionsAsync(tenantId, ct);

        var session = await _db.ActivitySessions.AsNoTracking()
            .Include(s => s.Activity)
            .Include(s => s.CoachUser)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId && !s.IsDeleted, ct);

        if (session?.Activity == null
            || session.Activity.Kind != ActivityKinds.Class
            || !session.Activity.IsActive
            || session.Activity.IsDeleted
            || !session.Activity.VisibleToMembers)
        {
            return Result<MemberClassDetailsDto>.Failure("Class not found / الحصة غير موجودة");
        }

        if (session.Status == ActivitySessionStatuses.Cancelled
            || session.StartsAtUtc < DateTime.UtcNow)
        {
            return Result<MemberClassDetailsDto>.Failure("Class not found / الحصة غير موجودة");
        }

        var bookedCount = await _db.ActivityBookings.AsNoTracking()
            .CountAsync(b => b.TenantId == tenantId
                           && b.SessionId == sessionId
                           && !b.IsDeleted
                           && (b.Status == ActivityBookingStatuses.Booked
                               || b.Status == ActivityBookingStatuses.CheckedIn), ct);

        return Result<MemberClassDetailsDto>.Success(ToDetailsDto(session, bookedCount));
    }

    private async Task TryGenerateSessionsAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            await _sessionGeneration.GenerateUpcomingSessionsAsync(tenantId, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Lazy session generation failed for tenant {TenantId}; continuing with existing rows",
                tenantId);
        }
    }

    private async Task<Dictionary<Guid, int>> LoadSeatOccupyingCountsAsync(
        Guid tenantId, IEnumerable<Guid> sessionIds, CancellationToken ct)
    {
        var ids = sessionIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, int>();

        return await _db.ActivityBookings.AsNoTracking()
            .Where(b => b.TenantId == tenantId
                        && !b.IsDeleted
                        && ids.Contains(b.SessionId)
                        && (b.Status == ActivityBookingStatuses.Booked
                            || b.Status == ActivityBookingStatuses.CheckedIn))
            .GroupBy(b => b.SessionId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    private static MemberClassListItemDto ToListItemDto(Core.Entities.ActivitySession session, int bookedCount)
    {
        var activity = session.Activity!;
        var (date, startTime, endTime) = ToCairoSchedule(session.StartsAtUtc, session.EndsAtUtc);
        var available = Math.Max(0, session.Capacity - bookedCount);

        return new MemberClassListItemDto
        {
            Id = session.Id,
            ActivityId = session.ActivityId,
            Name = activity.Name,
            NameAr = activity.NameAr,
            Description = activity.Description,
            TrainerId = session.CoachUserId,
            TrainerName = FormatCoachName(session.CoachUser),
            Date = date,
            StartTime = startTime,
            EndTime = endTime,
            DurationMinutes = (int)Math.Round((session.EndsAtUtc - session.StartsAtUtc).TotalMinutes),
            StartsAtUtc = session.StartsAtUtc,
            EndsAtUtc = session.EndsAtUtc,
            Price = activity.DropInPrice,
            Capacity = session.Capacity,
            BookedCount = bookedCount,
            AvailableSeats = available,
            Status = session.Status
        };
    }

    private static MemberClassDetailsDto ToDetailsDto(Core.Entities.ActivitySession session, int bookedCount)
    {
        var activity = session.Activity!;
        var (date, startTime, endTime) = ToCairoSchedule(session.StartsAtUtc, session.EndsAtUtc);
        var available = Math.Max(0, session.Capacity - bookedCount);

        MemberClassTrainerDto? trainer = null;
        if (session.CoachUserId.HasValue && session.CoachUser != null)
        {
            trainer = new MemberClassTrainerDto
            {
                Id = session.CoachUserId.Value,
                Name = FormatCoachName(session.CoachUser) ?? ""
            };
        }

        return new MemberClassDetailsDto
        {
            Id = session.Id,
            ActivityId = session.ActivityId,
            Name = activity.Name,
            NameAr = activity.NameAr,
            Description = activity.Description,
            DescriptionAr = activity.DescriptionAr,
            Schedule = new MemberClassScheduleDto
            {
                Date = date,
                StartTime = startTime,
                EndTime = endTime,
                DurationMinutes = (int)Math.Round((session.EndsAtUtc - session.StartsAtUtc).TotalMinutes),
                StartsAtUtc = session.StartsAtUtc,
                EndsAtUtc = session.EndsAtUtc
            },
            Trainer = trainer,
            Price = activity.DropInPrice,
            Availability = new MemberClassAvailabilityDto
            {
                Capacity = session.Capacity,
                BookedCount = bookedCount,
                AvailableSeats = available
            },
            Status = session.Status,
            BookingRequired = activity.BookingRequired
        };
    }

    private static (DateOnly Date, TimeOnly StartTime, TimeOnly EndTime) ToCairoSchedule(
        DateTime startsAtUtc, DateTime endsAtUtc)
    {
        var startLocal = TimeZoneInfo.ConvertTimeFromUtc(startsAtUtc, CairoTz);
        var endLocal = TimeZoneInfo.ConvertTimeFromUtc(endsAtUtc, CairoTz);
        return (
            DateOnly.FromDateTime(startLocal),
            TimeOnly.FromDateTime(startLocal),
            TimeOnly.FromDateTime(endLocal));
    }

    private static string? FormatCoachName(Core.Entities.AppUser? coach) =>
        coach == null ? null : $"{coach.FirstName} {coach.LastName}".Trim();

    /// <summary>
    /// Member endpoints require a linked GymMember profile (same chain as MemberBookingService).
    /// </summary>
    private async Task<string?> EnsureMemberProfileAsync(
        Guid tenantId, Guid identityUserId, CancellationToken ct)
    {
        var appUserId = await _db.AppUsers.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.UserId == identityUserId.ToString() && !u.IsDeleted)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
        if (appUserId == null)
            return "Member profile not found / لم يتم العثور على ملف العضو";

        var memberExists = await _db.GymMembers.AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && m.AppUserId == appUserId.Value && !m.IsDeleted, ct);

        return memberExists
            ? null
            : "Member profile not found / لم يتم العثور على ملف العضو";
    }
}
