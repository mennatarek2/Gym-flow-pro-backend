namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Activities;
using GMS.Application.DTOs.Notifications;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

public class SessionBookingService : ISessionBookingService
{
    private static readonly TimeZoneInfo CairoTz =
        TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly GymFlowProDbContext _db;
    private readonly IActivityEntitlementService _entitlements;
    private readonly ISessionGenerationService _sessionGeneration;
    private readonly IStaffNotificationPublisher? _staffNotifications;
    private readonly ILogger<SessionBookingService> _logger;

    public SessionBookingService(
        GymFlowProDbContext db,
        IActivityEntitlementService entitlements,
        ISessionGenerationService sessionGeneration,
        ILogger<SessionBookingService> logger,
        IStaffNotificationPublisher? staffNotifications = null)
    {
        _db = db;
        _entitlements = entitlements;
        _sessionGeneration = sessionGeneration;
        _logger = logger;
        _staffNotifications = staffNotifications;
    }

    public async Task<Result<List<SessionDto>>> GetSessionsByDateAsync(Guid tenantId, DateOnly date, CancellationToken ct = default)
    {
        // Materialize any missing sessions from schedules before listing (idempotent).
        // Fixes the gap where Activities/Schedules exist but Classes stay empty until the hourly job.
        try
        {
            await _sessionGeneration.GenerateUpcomingSessionsAsync(tenantId, null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lazy session generation failed for tenant {TenantId}; continuing with existing rows", tenantId);
        }

        // Schedules/sessions use Cairo local times stored as UTC — query the Cairo calendar day.
        var startUtc = CairoDayStartUtc(date);
        var endUtc = CairoDayStartUtc(date.AddDays(1));

        var sessions = await _db.ActivitySessions.AsNoTracking()
            .Include(s => s.Activity)
            .Include(s => s.CoachUser)
            .Include(s => s.Bookings)
            .Where(s => s.TenantId == tenantId
                        && s.StartsAtUtc >= startUtc
                        && s.StartsAtUtc < endUtc
                        && s.Activity != null
                        && s.Activity.Kind == ActivityKinds.Class
                        && !s.Activity.IsDeleted)
            .OrderBy(s => s.StartsAtUtc)
            .ToListAsync(ct);

        return Result<List<SessionDto>>.Success(sessions.Select(ToSessionDto).ToList());
    }

    private static DateTime CairoDayStartUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            CairoTz);
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
            RemainingCapacity = dto.RemainingCapacity,
            Status = dto.Status,
            CoachUserId = dto.CoachUserId,
            CoachName = dto.CoachName,
            Bookings = session.Bookings
                .Where(b => !b.IsDeleted && b.Status != ActivityBookingStatuses.Cancelled
                            && b.Status != ActivityBookingStatuses.CancelledLate)
                .OrderBy(b => b.CreatedAtUtc)
                .Select(ToBookingDto)
                .ToList()
        };
        await AttachInvoicesAsync(tenantId, detail.Bookings, ct);
        return Result<SessionDetailDto>.Success(detail);
    }

    /// <summary>
    /// Create a booking (staff or member source) with the full server-side validation chain:
    /// session bookable → duplicate → capacity → eligibility → quota → payment requirement →
    /// transactional create. Capacity is enforced transactionally (count re-checked inside the
    /// same SaveChanges plus a filtered unique index on active bookings per member/session).
    /// </summary>
    public async Task<Result<BookingDto>> CreateBookingAsync(Guid tenantId, CreateBookingRequest request, Guid? staffUserId, CancellationToken ct = default)
    {
        if (request.MemberId == Guid.Empty)
            request.MemberId = null;

        // 1-2) Session + bookability
        var session = await _db.ActivitySessions
            .Include(s => s.Bookings)
            .Include(s => s.Activity)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId && s.TenantId == tenantId, ct);
        if (session == null)
            return Result<BookingDto>.Failure("Session not found / الحصة غير موجودة");
        if (session.Status == ActivitySessionStatuses.Cancelled)
            return Result<BookingDto>.Failure("Session is cancelled / الحصة ملغاة");
        if (!session.Activity!.BookingRequired)
            return Result<BookingDto>.Failure("This activity does not require booking / هذا النشاط لا يحتاج حجز");
        if (session.EndsAtUtc <= DateTime.UtcNow)
            return Result<BookingDto>.Failure("Session already ended / انتهت الحصة");
        if (session.StartsAtUtc <= DateTime.UtcNow.AddHours(-1))
            return Result<BookingDto>.Failure("Session already started / بدأت الحصة");

        var guestName = request.GuestName?.Trim();
        var guestPhone = request.GuestPhone?.Trim();
        GymMember? member = null;
        if (request.MemberId.HasValue)
        {
            member = await _db.GymMembers.FirstOrDefaultAsync(
                m => m.Id == request.MemberId.Value && m.TenantId == tenantId && !m.IsDeleted, ct);
            if (member == null)
                return Result<BookingDto>.Failure("Member not found / العضو غير موجود");
        }
        else if (string.IsNullOrWhiteSpace(guestName) || string.IsNullOrWhiteSpace(guestPhone))
        {
            return Result<BookingDto>.Failure(
                "Guest name and phone are required / اسم ورقم هاتف الضيف مطلوبان");
        }
        else if (guestName!.Length > 200 || guestPhone!.Length > 30)
        {
            return Result<BookingDto>.Failure(
                "Guest name or phone is too long / اسم الضيف أو هاتفه طويل جداً");
        }

        // 3) Duplicate active booking
        if (request.MemberId.HasValue && session.Bookings.Any(b => !b.IsDeleted && b.MemberId == request.MemberId &&
                ActivityBookingStatuses.IsSeatOccupying(b.Status)))
            return Result<BookingDto>.Failure("Member already booked / العضو محجوز مسبقاً");

        // 4) Capacity (pre-check; final guard inside the transaction below)
        var activeBookings = session.Bookings.Count(b => !b.IsDeleted && ActivityBookingStatuses.IsSeatOccupying(b.Status));
        if (activeBookings >= session.Capacity)
            return Result<BookingDto>.Failure("Session is full / الحصة ممتلئة");

        // 5) Eligibility / entitlement
        Guid? coveringMembershipId = null;
        bool requiresDropIn = false;

        if (member != null)
        {
            var entitlement = await _entitlements.ResolveAsync(tenantId, member.Id, session.ActivityId, ct);
            if (entitlement != null && entitlement.IsEntitled)
            {
                // 6) Quota (limited mode only; single counting path via the entitlement service)
                var hasAvailableQuota = true;
                if (entitlement.Mode == ActivityEntitlementService.EligibilityLimited)
                {
                    var remaining = await _entitlements.RemainingQuotaAsync(
                        tenantId, member.Id, session.ActivityId, entitlement.CoveringMembership, ct);
                    if (remaining.HasValue && remaining.Value <= 0)
                        hasAvailableQuota = false;
                }

                // A paid drop-in request is explicit and is allowed when quota is exhausted
                // (or when the desk intentionally chooses paid access).
                if (request.SaleId.HasValue)
                    requiresDropIn = true;
                else if (!hasAvailableQuota)
                    return Result<BookingDto>.Failure(
                        "Monthly class quota exhausted / تم استهلاك الحصة الشهرية بالكامل");
                else
                    coveringMembershipId = entitlement.CoveringMembership.Id;
            }
            else
            {
                if ((session.Activity.DropInPrice ?? 0) <= 0 && !request.SaleId.HasValue)
                    return Result<BookingDto>.Failure(
                        "Member is not eligible for this activity and no drop-in price is set / العضو غير مشمول ولا يوجد سعر دخول فردي");
                requiresDropIn = true;
            }
        }
        else
        {
            // Guest bookings are always paid drop-ins.
            requiresDropIn = true;
        }

        if (requiresDropIn)
        {
            // 7) Drop-in path: payment must exist BEFORE booking creation (no partial states).
            if (!request.SaleId.HasValue)
                return Result<BookingDto>.Failure(
                    "Payment required before booking / يجب إتمام الدفع قبل الحجز");
            var saleOk = await _db.Sales.AsNoTracking()
                .AnyAsync(s => s.Id == request.SaleId.Value
                               && s.TenantId == tenantId
                               && s.MemberId == request.MemberId
                               && s.GuestName == guestName
                               && s.GuestPhone == guestPhone
                               && s.Status != "refunded"
                               && s.AmountDue <= 0
                               && !s.IsDeleted
                               && s.Lines.Any(l => l.LineType == "drop_in" && l.ReferenceId == session.ActivityId), ct);

            // A drop-in sale pays for exactly one session. Without this check the same paid
            // sale could back unlimited bookings across different sessions of the activity —
            // block reuse unless the prior booking it backed was cancelled in time (mirrors
            // the membership quota-consuming/refunding rule so paid-for access behaves the
            // same whether it came from a plan credit or a drop-in payment).
            if (saleOk)
            {
                var saleAlreadyConsumed = await _db.ActivityBookings.AsNoTracking()
                    .AnyAsync(b => b.TenantId == tenantId && b.SaleId == request.SaleId.Value && !b.IsDeleted
                                   && (b.Status == ActivityBookingStatuses.Booked
                                       || b.Status == ActivityBookingStatuses.CheckedIn
                                       || b.Status == ActivityBookingStatuses.CancelledLate
                                       || b.Status == ActivityBookingStatuses.NoShow), ct);
                if (saleAlreadyConsumed)
                    saleOk = false;
            }

            if (!saleOk)
                return Result<BookingDto>.Failure(
                    "No valid drop-in payment for this class / لا يوجد دفع دخول فردي صالح لهذا النشاط");
        }
        // 8) Transactional create — capacity re-checked under the same save.
        var booking = new ActivityBooking
        {
            TenantId = tenantId,
            SessionId = session.Id,
            MemberId = request.MemberId,
            GuestName = member == null ? guestName : null,
            GuestPhone = member == null ? guestPhone : null,
            Status = ActivityBookingStatuses.Booked,
            Source = string.IsNullOrWhiteSpace(request.Source) ? "staff" : request.Source.Trim().ToLowerInvariant(),
            CoveringMembershipId = coveringMembershipId,
            SaleId = requiresDropIn ? request.SaleId : null,
            CreatedAtUtc = DateTime.UtcNow
        };

        // Serialize concurrent bookings for this session before re-counting capacity:
        // SQL Server takes UPDLOCK on the session row (released at commit); other
        // providers (tests) take the optimistic path below.
        if (_db.Database.IsSqlServer())
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM [activity_sessions] WITH (UPDLOCK, ROWLOCK) WHERE [id] = {session.Id} AND [TenantId] = {tenantId}", ct);

                var currentActive = await _db.ActivityBookings.AsNoTracking()
                    .Where(b => b.TenantId == tenantId && b.SessionId == session.Id && !b.IsDeleted
                                && (b.Status == ActivityBookingStatuses.Booked || b.Status == ActivityBookingStatuses.CheckedIn))
                    .CountAsync(ct);
                if (currentActive >= session.Capacity)
                {
                    await tx.RollbackAsync(ct);
                    return Result<BookingDto>.Failure("Session is full / الحصة ممتلئة");
                }

                _db.ActivityBookings.Add(booking);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogWarning(ex, "Booking insert conflict for session {SessionId}", session.Id);
                return Result<BookingDto>.Failure("Session is full or booking conflict — please retry / الحصة ممتلئة أو تعارض في الحجز، أعد المحاولة");
            }
        }
        else
        {
            var optimistic = await CreateBookingOptimisticAsync(tenantId, booking, session.Capacity, ct);
            if (!optimistic.IsSuccess)
                return optimistic;
        }

        _logger.LogInformation("Booking {BookingId} created for session {SessionId} member {MemberId} guest {GuestName}",
            booking.Id, session.Id, request.MemberId, guestName);

        if (booking.MemberId.HasValue && booking.Member == null)
            await _db.Entry(booking).Reference(b => b.Member).LoadAsync(ct);

        // Prefer ClassFull over BookingNew (avoid spam on every book).
        await TryNotifyClassFullAsync(tenantId, session, ct);

        var created = ToBookingDto(booking);
        await AttachInvoicesAsync(tenantId, new List<BookingDto> { created }, ct);
        return Result<BookingDto>.Success(created);
    }

    private async Task TryNotifyClassFullAsync(Guid tenantId, ActivitySession session, CancellationToken ct)
    {
        if (_staffNotifications == null) return;

        var activeCount = await _db.ActivityBookings.AsNoTracking()
            .CountAsync(b => b.TenantId == tenantId && b.SessionId == session.Id && !b.IsDeleted
                && (b.Status == ActivityBookingStatuses.Booked || b.Status == ActivityBookingStatuses.CheckedIn), ct);
        if (activeCount < session.Capacity) return;

        await _staffNotifications.TryPublishAsync(tenantId, new CreateStaffNotificationRequest
        {
            Type = StaffNotificationTypes.ClassFull,
            Category = StaffNotificationCategories.Classes,
            Priority = StaffNotificationPriorities.ActionRequired,
            Title = "Class is full",
            TitleAr = "الحصة ممتلئة",
            Body = "A class session has reached capacity.",
            BodyAr = "وصلت حصة إلى السعة الكاملة.",
            EntityType = "ActivitySession",
            EntityId = session.Id,
            ActionUrl = $"/dashboard/classes/?sessionId={session.Id}",
            DedupeKey = $"class-full:{session.Id:N}",
            RecipientRoles = new[] { "Owner", "Manager", "Receptionist" },
            RecipientAppUserIds = session.CoachUserId.HasValue ? new[] { session.CoachUserId.Value } : null
        });
    }

    private async Task<Result<BookingDto>> CreateBookingOptimisticAsync(
        Guid tenantId, ActivityBooking booking, int capacity, CancellationToken ct)
    {
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var currentActive = await _db.ActivityBookings
                .Where(b => b.TenantId == tenantId && b.SessionId == booking.SessionId && !b.IsDeleted
                            && (b.Status == ActivityBookingStatuses.Booked || b.Status == ActivityBookingStatuses.CheckedIn))
                .CountAsync(ct);
            if (currentActive >= capacity)
            {
                await tx.RollbackAsync(ct);
                return Result<BookingDto>.Failure("Session is full / الحصة ممتلئة");
            }

            _db.ActivityBookings.Add(booking);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Booking insert conflict for session {SessionId}", booking.SessionId);
            return Result<BookingDto>.Failure("Session is full or booking conflict — please retry / الحصة ممتلئة أو تعارض في الحجز، أعد المحاولة");
        }

        if (booking.MemberId.HasValue)
            await _db.Entry(booking).Reference(b => b.Member).LoadAsync(ct);
        return Result<BookingDto>.Success(ToBookingDto(booking));
    }

    public async Task<Result<BookingDto>> CancelBookingAsync(Guid tenantId, Guid bookingId, CancellationToken ct = default)
        => await CancelCoreAsync(tenantId, bookingId, cancelledByStaff: true, ct);

    public async Task<Result<MemberCancelPolicyDto>> GetCancelPolicyAsync(Guid tenantId, Guid bookingId, CancellationToken ct = default)
    {
        var booking = await _db.ActivityBookings.AsNoTracking()
            .Include(b => b.Session)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.TenantId == tenantId && !b.IsDeleted, ct);
        if (booking == null)
            return Result<MemberCancelPolicyDto>.Failure("Booking not found / الحجز غير موجود");
        var hours = await LateCancellationHours(tenantId, ct);
        var deadline = booking.Session!.StartsAtUtc.AddHours(-hours);
        return Result<MemberCancelPolicyDto>.Success(new MemberCancelPolicyDto
        {
            RefundDeadlineUtc = deadline,
            LateCancellationHours = hours,
            IsLate = DateTime.UtcNow > deadline
        });
    }

    public async Task<Result<BookingDto>> CancelOwnBookingAsync(Guid tenantId, Guid memberId, Guid bookingId, CancellationToken ct = default)
        => await CancelCoreAsync(tenantId, bookingId, cancelledByStaff: false, ct, requireMemberId: memberId);

    private async Task<Result<BookingDto>> CancelCoreAsync(
        Guid tenantId, Guid bookingId, bool cancelledByStaff, CancellationToken ct, Guid? requireMemberId = null)
    {
        var booking = await _db.ActivityBookings
            .Include(b => b.Member)
            .Include(b => b.Session)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.TenantId == tenantId && !b.IsDeleted, ct);
        if (booking == null)
            return Result<BookingDto>.Failure("Booking not found / الحجز غير موجود");
        if (requireMemberId.HasValue && booking.MemberId != requireMemberId.Value)
            return Result<BookingDto>.Failure("Booking not found / الحجز غير موجود"); // don't leak other members' bookings
        if (ActivityBookingStatuses.IsQuotaConsuming(booking.Status) && booking.Status != ActivityBookingStatuses.Booked)
            return Result<BookingDto>.Failure($"Cannot cancel a booking in status '{booking.Status}' / لا يمكن إلغاء حجز بهذه الحالة");
        if (booking.Status == ActivityBookingStatuses.Cancelled || booking.Status == ActivityBookingStatuses.CancelledLate)
            return Result<BookingDto>.Failure("Booking already cancelled / الحجز ملغى");
        if (booking.Session!.EndsAtUtc <= DateTime.UtcNow)
            return Result<BookingDto>.Failure("Session already ended / انتهت الحصة");

        var lateHours = await LateCancellationHours(tenantId, ct);
        var deadline = booking.Session.StartsAtUtc.AddHours(-lateHours);
        var isLate = DateTime.UtcNow > deadline;

        booking.Status = isLate ? ActivityBookingStatuses.CancelledLate : ActivityBookingStatuses.Cancelled;
        booking.CancelledAtUtc = DateTime.UtcNow;
        booking.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Booking {BookingId} cancelled (late={Late}, staff={Staff})",
            booking.Id, isLate, cancelledByStaff);

        if (_staffNotifications != null)
        {
            var name = booking.Member?.FullName ?? booking.GuestName ?? "Guest walk-in";
            await _staffNotifications.TryPublishAsync(tenantId, new CreateStaffNotificationRequest
            {
                Type = StaffNotificationTypes.BookingCancelled,
                Category = StaffNotificationCategories.Bookings,
                Priority = StaffNotificationPriorities.ActionRequired,
                Title = "Booking cancelled",
                TitleAr = "تم إلغاء حجز",
                Body = $"{name} cancelled a class booking.",
                BodyAr = $"ألغى {name} حجز حصة.",
                EntityType = "ActivityBooking",
                EntityId = booking.Id,
                ActionUrl = $"/dashboard/classes/?sessionId={booking.SessionId}",
                DedupeKey = $"booking-cancelled:{booking.Id:N}",
                RecipientRoles = new[] { "Owner", "Manager", "Receptionist" },
                RecipientAppUserIds = booking.Session?.CoachUserId.HasValue == true
                    ? new[] { booking.Session.CoachUserId.Value }
                    : null
            });
        }

        return Result<BookingDto>.Success(ToBookingDto(booking));
    }

    public async Task<Result<BookingDto>> CheckInBookingAsync(Guid tenantId, Guid bookingId, Guid staffUserId, CancellationToken ct = default)
    {
        var booking = await _db.ActivityBookings
            .Include(b => b.Member)
            .Include(b => b.Session)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.TenantId == tenantId && !b.IsDeleted, ct);
        if (booking == null)
            return Result<BookingDto>.Failure("Booking not found / الحجز غير موجود");
        if (booking.Status != ActivityBookingStatuses.Booked)
            return Result<BookingDto>.Failure("Only booked members can check in / التسجيل للمحجوزين فقط");

        // Duplicate check-in prevention: one GymAttendance row per booking.
        var existingAttendance = await _db.GymAttendances.AsNoTracking()
            .AnyAsync(a => a.TenantId == tenantId && a.BookingId == booking.Id && !a.IsDeleted, ct);
        if (existingAttendance)
            return Result<BookingDto>.Failure("Already checked in / مسجل حضور مسبقاً");

        // CheckedInByUserId is AppUser.Id (domain) — callers pass the Identity JWT sub. Resolve it.
        var staffAppUserId = await _db.AppUsers.AsNoTracking()
            .Where(u => u.TenantId == tenantId && (u.UserId == staffUserId.ToString() || u.Id == staffUserId) && !u.IsDeleted)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
        if (staffAppUserId == null)
            return Result<BookingDto>.Failure("Staff user not found / لم يتم العثور على المستخدم");

        booking.Status = ActivityBookingStatuses.CheckedIn;
        booking.CheckedInAtUtc = DateTime.UtcNow;
        booking.CheckedInByUserId = staffAppUserId.Value;
        booking.UpdatedAtUtc = DateTime.UtcNow;

        // Integrate with the existing Attendance system — one record, no second attendance system.
        var attendance = new GymAttendance
        {
            TenantId = tenantId,
            MemberId = booking.MemberId,
            GuestName = booking.GuestName,
            GuestPhone = booking.GuestPhone,
            MembershipId = booking.CoveringMembershipId,
            BookingId = booking.Id,
            SessionId = booking.SessionId,
            StaffUserId = staffAppUserId.Value,
            CheckInAtUtc = DateTime.UtcNow,
            EntryMethod = "manual",
            ManualReason = $"Class check-in: {booking.Session?.Activity?.Name}",
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.GymAttendances.Add(attendance);
        booking.Attendance = attendance; // navigation lets EF order INSERT before UPDATE

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Check-in race for booking {BookingId}", booking.Id);
            return Result<BookingDto>.Failure("Already checked in / مسجل حضور مسبقاً");
        }

        return Result<BookingDto>.Success(ToBookingDto(booking));
    }

    private static SessionDto ToSessionDto(ActivitySession s)
    {
        var bookings = s.Bookings.Where(b => !b.IsDeleted && b.Status != ActivityBookingStatuses.Cancelled
                                             && b.Status != ActivityBookingStatuses.CancelledLate).ToList();
        var bookedCount = bookings.Count(b => b.Status is "booked" or "checked_in");
        return new SessionDto
        {
            Id = s.Id,
            ActivityId = s.ActivityId,
            ActivityName = s.Activity?.Name ?? "",
            ActivityKind = s.Activity?.Kind ?? "",
            StartsAtUtc = s.StartsAtUtc,
            EndsAtUtc = s.EndsAtUtc,
            Capacity = s.Capacity,
            BookedCount = bookedCount,
            CheckedInCount = bookings.Count(b => b.Status == "checked_in"),
            RemainingCapacity = Math.Max(0, s.Capacity - bookedCount),
            Status = s.Status,
            CoachUserId = s.CoachUserId,
            CoachName = s.CoachUser == null ? null : $"{s.CoachUser.FirstName} {s.CoachUser.LastName}".Trim()
        };
    }

    internal static BookingDto ToBookingDto(ActivityBooking b) => new()
    {
        Id = b.Id,
        SessionId = b.SessionId,
        MemberId = b.MemberId,
        MemberName = b.Member?.FullName ?? b.GuestName ?? "Guest walk-in",
        MemberPhone = b.Member?.PhoneNumber ?? b.GuestPhone,
        GuestName = b.GuestName,
        GuestPhone = b.GuestPhone,
        Status = b.Status,
        Source = b.Source,
        SaleId = b.SaleId,
        CheckedInAtUtc = b.CheckedInAtUtc
    };

    private async Task AttachInvoicesAsync(Guid tenantId, List<BookingDto> bookings, CancellationToken ct)
    {
        var saleIds = bookings
            .Where(b => b.SaleId.HasValue)
            .Select(b => b.SaleId!.Value)
            .Distinct()
            .ToList();
        if (saleIds.Count == 0) return;

        var invoices = await _db.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenantId
                        && i.SaleId != null
                        && saleIds.Contains(i.SaleId.Value)
                        && i.Type == "invoice"
                        && i.Status != "voided")
            .Select(i => new { i.SaleId, i.Id, i.InvoiceNumber })
            .ToListAsync(ct);

        foreach (var booking in bookings)
        {
            if (!booking.SaleId.HasValue) continue;
            var invoice = invoices.FirstOrDefault(i => i.SaleId == booking.SaleId);
            if (invoice == null) continue;
            booking.InvoiceId = invoice.Id;
            booking.InvoiceNumber = invoice.InvoiceNumber;
        }
    }

    private async Task<int> LateCancellationHours(Guid tenantId, CancellationToken ct)
    {
        try
        {
            var raw = await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => t.Settings)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty(TenantSettingsKeys.LateCancellationHours, out var el)
                    && el.TryGetInt32(out var v))
                    return Math.Clamp(v, 0, 48);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // fall through to default
        }
        return DefaultLateCancellationHours;
    }

    internal const int DefaultLateCancellationHours = 2;
}
