namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Activities;
using GMS.Application.Interfaces;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

public class SessionBookingService : ISessionBookingService
{
    private readonly GymFlowProDbContext _db;
    private readonly ILogger<SessionBookingService> _logger;

    public SessionBookingService(GymFlowProDbContext db, ILogger<SessionBookingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<List<SessionDto>>> GetSessionsByDateAsync(Guid tenantId, DateOnly date, CancellationToken ct = default)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddDays(1);

        var sessions = await _db.ActivitySessions.AsNoTracking()
            .Include(s => s.Activity)
            .Include(s => s.CoachUser)
            .Include(s => s.Bookings)
            .Where(s => s.TenantId == tenantId && s.StartsAtUtc >= start && s.StartsAtUtc < end)
            .OrderBy(s => s.StartsAtUtc)
            .ToListAsync(ct);

        return Result<List<SessionDto>>.Success(sessions.Select(ToSessionDto).ToList());
    }

    public async Task<Result<SessionDetailDto>> GetSessionDetailAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.ActivitySessions.AsNoTracking()
            .Include(s => s.Activity)
            .Include(s => s.CoachUser)
            .Include(s => s.Bookings).ThenInclude(b => b.Member)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId, ct);

        if (session == null)
            return Result<SessionDetailDto>.Failure("Session not found / الحصة غير موجودة");

        var dto = ToSessionDto(session);
        var detail = new SessionDetailDto
        {
            Id = dto.Id,
            ActivityId = dto.ActivityId,
            ActivityName = dto.ActivityName,
            ActivityKind = dto.ActivityKind,
            StartsAtUtc = dto.StartsAtUtc,
            EndsAtUtc = dto.EndsAtUtc,
            Capacity = dto.Capacity,
            BookedCount = dto.BookedCount,
            CheckedInCount = dto.CheckedInCount,
            Status = dto.Status,
            CoachName = dto.CoachName,
            Bookings = session.Bookings
                .Where(b => !b.IsDeleted && b.Status != "cancelled")
                .OrderBy(b => b.CreatedAtUtc)
                .Select(ToBookingDto)
                .ToList()
        };
        return Result<SessionDetailDto>.Success(detail);
    }

    public async Task<Result<BookingDto>> CreateBookingAsync(Guid tenantId, CreateBookingRequest request, Guid? staffUserId, CancellationToken ct = default)
    {
        var session = await _db.ActivitySessions
            .Include(s => s.Bookings)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId && s.TenantId == tenantId, ct);
        if (session == null)
            return Result<BookingDto>.Failure("Session not found / الحصة غير موجودة");
        if (session.Status == "cancelled")
            return Result<BookingDto>.Failure("Session is cancelled / الحصة ملغاة");

        var member = await _db.GymMembers.FirstOrDefaultAsync(m => m.Id == request.MemberId && m.TenantId == tenantId, ct);
        if (member == null)
            return Result<BookingDto>.Failure("Member not found / العضو غير موجود");

        var activeBookings = session.Bookings.Count(b => !b.IsDeleted && b.Status is "booked" or "checked_in");
        if (activeBookings >= session.Capacity)
            return Result<BookingDto>.Failure("Session is full / الحصة ممتلئة");

        var duplicate = session.Bookings.Any(b => !b.IsDeleted && b.MemberId == request.MemberId && b.Status is "booked" or "checked_in");
        if (duplicate)
            return Result<BookingDto>.Failure("Member already booked / العضو محجوز مسبقاً");

        var booking = new ActivityBooking
        {
            TenantId = tenantId,
            SessionId = session.Id,
            MemberId = request.MemberId,
            Status = "booked",
            Source = string.IsNullOrWhiteSpace(request.Source) ? "staff" : request.Source.Trim().ToLowerInvariant(),
            CoveringMembershipId = request.CoveringMembershipId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ActivityBookings.Add(booking);
        await _db.SaveChangesAsync(ct);

        await _db.Entry(booking).Reference(b => b.Member).LoadAsync(ct);
        _logger.LogInformation("Booking {BookingId} created for session {SessionId}", booking.Id, session.Id);
        return Result<BookingDto>.Success(ToBookingDto(booking));
    }

    public async Task<Result<BookingDto>> CancelBookingAsync(Guid tenantId, Guid bookingId, CancellationToken ct = default)
    {
        var booking = await _db.ActivityBookings
            .Include(b => b.Member)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.TenantId == tenantId, ct);
        if (booking == null)
            return Result<BookingDto>.Failure("Booking not found / الحجز غير موجود");
        if (booking.Status == "cancelled")
            return Result<BookingDto>.Failure("Booking already cancelled / الحجز ملغى");

        booking.Status = "cancelled";
        booking.CancelledAtUtc = DateTime.UtcNow;
        booking.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result<BookingDto>.Success(ToBookingDto(booking));
    }

    public async Task<Result<BookingDto>> CheckInBookingAsync(Guid tenantId, Guid bookingId, Guid staffUserId, CancellationToken ct = default)
    {
        var booking = await _db.ActivityBookings
            .Include(b => b.Member)
            .Include(b => b.Session)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.TenantId == tenantId, ct);
        if (booking == null)
            return Result<BookingDto>.Failure("Booking not found / الحجز غير موجود");
        if (booking.Status != "booked")
            return Result<BookingDto>.Failure("Only booked members can check in / التسجيل للمحجوزين فقط");

        booking.Status = "checked_in";
        booking.CheckedInAtUtc = DateTime.UtcNow;
        booking.CheckedInByUserId = staffUserId;
        booking.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result<BookingDto>.Success(ToBookingDto(booking));
    }

    private static SessionDto ToSessionDto(ActivitySession s)
    {
        var bookings = s.Bookings.Where(b => !b.IsDeleted && b.Status != "cancelled").ToList();
        return new SessionDto
        {
            Id = s.Id,
            ActivityId = s.ActivityId,
            ActivityName = s.Activity?.Name ?? "",
            ActivityKind = s.Activity?.Kind ?? "",
            StartsAtUtc = s.StartsAtUtc,
            EndsAtUtc = s.EndsAtUtc,
            Capacity = s.Capacity,
            BookedCount = bookings.Count(b => b.Status is "booked" or "checked_in"),
            CheckedInCount = bookings.Count(b => b.Status == "checked_in"),
            Status = s.Status,
            CoachName = s.CoachUser == null ? null : $"{s.CoachUser.FirstName} {s.CoachUser.LastName}".Trim()
        };
    }

    private static BookingDto ToBookingDto(ActivityBooking b) => new()
    {
        Id = b.Id,
        SessionId = b.SessionId,
        MemberId = b.MemberId,
        MemberName = b.Member?.FullName ?? "",
        MemberPhone = b.Member?.PhoneNumber,
        Status = b.Status,
        Source = b.Source,
        CheckedInAtUtc = b.CheckedInAtUtc
    };
}
