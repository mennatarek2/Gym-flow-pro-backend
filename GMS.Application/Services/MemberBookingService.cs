namespace GMS.Application.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Activities;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Application.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Member App booking flows: discover activities, list sessions, book, cancel, my bookings.
/// All member identity is resolved server-side from the Identity user id (JWT sub) —
/// a member can only ever see and manipulate their own rows.
/// </summary>
public class MemberBookingService : IMemberBookingService
{
    private readonly GymFlowProDbContext _db;
    private readonly IActivityEntitlementService _entitlements;
    private readonly ISessionBookingService _bookings;

    public MemberBookingService(GymFlowProDbContext db, IActivityEntitlementService entitlements,
        ISessionBookingService bookings)
    {
        _db = db;
        _entitlements = entitlements;
        _bookings = bookings;
    }

    public async Task<Result<List<MemberActivityDto>>> ListActivitiesAsync(Guid tenantId, Guid identityUserId, CancellationToken ct = default)
    {
        var memberId = await ResolveMemberIdAsync(tenantId, identityUserId, ct);
        if (memberId == null)
            return Result<List<MemberActivityDto>>.Failure("Member profile not found / لم يتم العثور على ملف العضو");

        var activities = await _db.Activities.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.IsActive && !a.IsDeleted && a.VisibleToMembers)
            .OrderBy(a => a.Kind).ThenBy(a => a.Name)
            .ToListAsync(ct);

        // Distinct covering memberships → resolve eligibility per activity once per plan.
        var today = MembershipOperational.TodayCairo();
        var memberships = await _db.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.MemberId == memberId.Value && !m.IsDeleted)
            .ToListAsync(ct);
        var covering = MembershipOperational.SelectCoveringToday(memberships, today);

        List<PlanEntitlement>? planEntitlements = null;
        if (covering != null)
        {
            planEntitlements = await _db.PlanEntitlements.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.PlanId == covering.PlanId && !e.IsDeleted)
                .ToListAsync(ct);
        }

        var dtos = new List<MemberActivityDto>(activities.Count);
        foreach (var activity in activities)
        {
            var dto = new MemberActivityDto
            {
                Id = activity.Id,
                Name = activity.Name,
                NameAr = activity.NameAr,
                Description = activity.Description,
                DescriptionAr = activity.DescriptionAr,
                Kind = activity.Kind,
                BookingRequired = activity.BookingRequired,
                DropInPrice = activity.DropInPrice
            };

            var ent = planEntitlements?.FirstOrDefault(e => e.ActivityId == activity.Id);
            if (covering == null || ent == null)
            {
                dto.Eligibility = activity.DropInPrice.HasValue && activity.DropInPrice.Value > 0
                    ? ActivityEntitlementService.EligibilityDropIn
                    : ActivityEntitlementService.EligibilityNotEntitled;
            }
            else
            {
                dto.Eligibility = ent.AccessMode switch
                {
                    "limited" => ActivityEntitlementService.EligibilityLimited,
                    "unlimited" => ActivityEntitlementService.EligibilityUnlimited,
                    "included" => ActivityEntitlementService.EligibilityIncluded,
                    _ => ActivityEntitlementService.EligibilityNotEntitled
                };
                if (dto.Eligibility == ActivityEntitlementService.EligibilityLimited)
                {
                    var remaining = await _entitlements.RemainingQuotaAsync(
                        tenantId, memberId.Value, activity.Id, covering, ct);
                    dto.QuotaRemaining = remaining;
                    dto.QuotaLimit = ent.QuotaLimit;
                }
            }

            dtos.Add(dto);
        }

        return Result<List<MemberActivityDto>>.Success(dtos);
    }

    public async Task<Result<List<MemberSessionDto>>> ListUpcomingSessionsAsync(
        Guid tenantId, Guid identityUserId, Guid? activityId, DateTime? fromUtc, CancellationToken ct = default)
    {
        var memberId = await ResolveMemberIdAsync(tenantId, identityUserId, ct);
        if (memberId == null)
            return Result<List<MemberSessionDto>>.Failure("Member profile not found / لم يتم العثور على ملف العضو");

        var now = DateTime.UtcNow;
        var from = fromUtc.HasValue && fromUtc.Value > now ? fromUtc.Value : now;

        var query = _db.ActivitySessions.AsNoTracking()
            .Include(s => s.Activity)
            .Include(s => s.CoachUser)
            .Include(s => s.Bookings)
            .Where(s => s.TenantId == tenantId
                        && s.StartsAtUtc >= from
                        && s.Status != "cancelled"
                        && s.Activity!.IsActive
                        && s.Activity.VisibleToMembers
                        && !s.IsDeleted);
        if (activityId.HasValue)
            query = query.Where(s => s.ActivityId == activityId.Value);

        var sessions = await query.OrderBy(s => s.StartsAtUtc).Take(100).ToListAsync(ct);

        var myBookings = await _db.ActivityBookings.AsNoTracking()
            .Where(b => b.TenantId == tenantId && b.MemberId == memberId.Value && !b.IsDeleted
                        && sessions.Select(s => s.Id).Contains(b.SessionId))
            .ToDictionaryAsync(b => b.SessionId, b => b, ct);

        var entitlementCache = new Dictionary<Guid, ActivityEntitlement?>();
        var result = new List<MemberSessionDto>(sessions.Count);
        foreach (var session in sessions)
        {
            if (!entitlementCache.TryGetValue(session.ActivityId, out var ent))
            {
                ent = await _entitlements.ResolveAsync(tenantId, memberId.Value, session.ActivityId, ct);
                entitlementCache[session.ActivityId] = ent;
            }

            var bookedCount = session.Bookings.Count(b => !b.IsDeleted &&
                (b.Status == "booked" || b.Status == "checked_in"));
            var mine = myBookings.TryGetValue(session.Id, out var mb) ? mb : null;

            string? cannotBook = null;
            if (mine != null && (mine.Status == "booked" || mine.Status == "checked_in"))
                cannotBook = "already_booked";
            else if (bookedCount >= session.Capacity)
                cannotBook = "full";
            else if (ent == null || !ent.IsEntitled)
                cannotBook = session.Activity!.DropInPrice > 0 ? null : "not_entitled"; // drop-in still bookable after payment

            result.Add(new MemberSessionDto
            {
                Id = session.Id,
                ActivityId = session.ActivityId,
                ActivityName = session.Activity?.Name ?? "",
                ActivityKind = session.Activity?.Kind ?? "",
                StartsAtUtc = session.StartsAtUtc,
                EndsAtUtc = session.EndsAtUtc,
                Capacity = session.Capacity,
                BookedCount = bookedCount,
                CheckedInCount = 0,
                Status = session.Status,
                CoachName = session.CoachUser == null ? null : $"{session.CoachUser.FirstName} {session.CoachUser.LastName}".Trim(),
                MyBooking = mine != null && (mine.Status == "booked" || mine.Status == "checked_in") ? mine.Id : Guid.Empty,
                MyBookingStatus = mine?.Status,
                CanBook = cannotBook == null,
                CannotBookReason = cannotBook
            });
        }

        return Result<List<MemberSessionDto>>.Success(result);
    }

    public async Task<Result<MemberBookingDto>> BookAsync(Guid tenantId, Guid identityUserId, Guid sessionId, CancellationToken ct = default)
    {
        var memberId = await ResolveMemberIdAsync(tenantId, identityUserId, ct);
        if (memberId == null)
            return Result<MemberBookingDto>.Failure("Member profile not found / لم يتم العثور على ملف العضو");

        // Drop-in sale resolution happens inside CreateBookingAsync when the member has no
        // entitlement — the member app passes no SaleId; an unpaid drop-in is rejected with a
        // clear message so the app can route to payment first.
        var request = new CreateBookingRequest { SessionId = sessionId, MemberId = memberId.Value, Source = "member_app" };
        var result = await _bookings.CreateBookingAsync(tenantId, request, staffUserId: null, ct);
        if (!result.IsSuccess)
            return Result<MemberBookingDto>.Failure(result.Error!);

        return Result<MemberBookingDto>.Success(await ToMemberBookingDtoAsync(tenantId, result.Data!, ct));
    }

    /// <summary>Staff/reception books on behalf of a paid drop-in customer (sale created at POS first).</summary>
    public async Task<Result<MemberBookingDto>> BookWithSaleAsync(
        Guid tenantId, Guid memberId, Guid sessionId, Guid saleId, CancellationToken ct = default)
    {
        var request = new CreateBookingRequest
        {
            SessionId = sessionId,
            MemberId = memberId,
            SaleId = saleId,
            Source = "drop_in",
            CoveringMembershipId = null
        };
        var result = await _bookings.CreateBookingAsync(tenantId, request, staffUserId: null, ct);
        if (!result.IsSuccess)
            return Result<MemberBookingDto>.Failure(result.Error!);
        return Result<MemberBookingDto>.Success(await ToMemberBookingDtoAsync(tenantId, result.Data!, ct));
    }

    public async Task<Result<MemberCancelPolicyDto>> GetCancelPolicyAsync(Guid tenantId, Guid identityUserId, Guid bookingId, CancellationToken ct = default)
    {
        var memberId = await ResolveMemberIdAsync(tenantId, identityUserId, ct);
        if (memberId == null)
            return Result<MemberCancelPolicyDto>.Failure("Member profile not found / لم يتم العثور على ملف العضو");

        var owns = await _db.ActivityBookings.AsNoTracking()
            .AnyAsync(b => b.Id == bookingId && b.TenantId == tenantId && b.MemberId == memberId.Value, ct);
        if (!owns)
            return Result<MemberCancelPolicyDto>.Failure("Booking not found / الحجز غير موجود");

        return await _bookings.GetCancelPolicyAsync(tenantId, bookingId, ct);
    }

    public async Task<Result<MemberBookingDto>> CancelOwnAsync(Guid tenantId, Guid identityUserId, Guid bookingId, CancellationToken ct = default)
    {
        var memberId = await ResolveMemberIdAsync(tenantId, identityUserId, ct);
        if (memberId == null)
            return Result<MemberBookingDto>.Failure("Member profile not found / لم يتم العثور على ملف العضو");

        var result = await _bookings.CancelOwnBookingAsync(tenantId, memberId.Value, bookingId, ct);
        if (!result.IsSuccess)
            return Result<MemberBookingDto>.Failure(result.Error!);

        var dto = await ToMemberBookingDtoAsync(tenantId, result.Data!, ct);
        return Result<MemberBookingDto>.Success(dto);
    }

    public async Task<Result<List<MemberBookingDto>>> MyBookingsAsync(Guid tenantId, Guid identityUserId, CancellationToken ct = default)
    {
        var memberId = await ResolveMemberIdAsync(tenantId, identityUserId, ct);
        if (memberId == null)
            return Result<List<MemberBookingDto>>.Failure("Member profile not found / لم يتم العذو على ملف العضو");

        var bookings = await _db.ActivityBookings.AsNoTracking()
            .Include(b => b.Session).ThenInclude(s => s!.Activity)
            .Include(b => b.Session).ThenInclude(s => s!.CoachUser)
            .Where(b => b.TenantId == tenantId && b.MemberId == memberId.Value && !b.IsDeleted)
            .OrderByDescending(b => b.Session!.StartsAtUtc)
            .Take(200)
            .ToListAsync(ct);

        var covering = await CoveringMembershipAsync(tenantId, memberId.Value, ct);
        var result = new List<MemberBookingDto>(bookings.Count);
        foreach (var b in bookings)
        {
            var dto = ToMemberBookingDto(b);
            dto.QuotaConsumed = Core.Constants.ActivityBookingStatuses.IsQuotaConsuming(b.Status);
            if (covering != null && dto.QuotaConsumed && b.CoveringMembershipId == covering.Id)
            {
                dto.QuotaRemaining = await _entitlements.RemainingQuotaAsync(
                    tenantId, memberId.Value, b.Session!.ActivityId, covering, ct);
            }
            result.Add(dto);
        }
        return Result<List<MemberBookingDto>>.Success(result);
    }

    public async Task<Result<MemberBookingDto>> MyBookingAsync(Guid tenantId, Guid identityUserId, Guid bookingId, CancellationToken ct = default)
    {
        var memberId = await ResolveMemberIdAsync(tenantId, identityUserId, ct);
        if (memberId == null)
            return Result<MemberBookingDto>.Failure("Member profile not found / لم يتم العذو على ملف العضو");

        var booking = await _db.ActivityBookings.AsNoTracking()
            .Include(b => b.Session).ThenInclude(s => s!.Activity)
            .Include(b => b.Session).ThenInclude(s => s!.CoachUser)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.TenantId == tenantId && b.MemberId == memberId.Value && !b.IsDeleted, ct);
        if (booking == null)
            return Result<MemberBookingDto>.Failure("Booking not found / الحجز غير موجود");

        var dto = ToMemberBookingDto(booking);
        dto.QuotaConsumed = Core.Constants.ActivityBookingStatuses.IsQuotaConsuming(booking.Status);
        var covering = await CoveringMembershipAsync(tenantId, memberId.Value, ct);
        if (covering != null && dto.QuotaConsumed && booking.CoveringMembershipId == covering.Id)
        {
            dto.QuotaRemaining = await _entitlements.RemainingQuotaAsync(
                tenantId, memberId.Value, booking.Session!.ActivityId, covering, ct);
        }
        return Result<MemberBookingDto>.Success(dto);
    }

    private static MemberBookingDto ToMemberBookingDto(ActivityBooking b) => new()
    {
        Id = b.Id,
        SessionId = b.SessionId,
        Status = b.Status,
        ActivityId = b.Session!.ActivityId,
        ActivityName = b.Session.Activity?.Name ?? "",
        ActivityKind = b.Session.Activity?.Kind ?? "",
        StartsAtUtc = b.Session.StartsAtUtc,
        EndsAtUtc = b.Session.EndsAtUtc,
        CoachName = b.Session.CoachUser == null ? null : $"{b.Session.CoachUser.FirstName} {b.Session.CoachUser.LastName}".Trim(),
        CreatedAtUtc = b.CreatedAtUtc,
        CancelledAtUtc = b.CancelledAtUtc,
        CheckedInAtUtc = b.CheckedInAtUtc
    };

    private async Task<MemberBookingDto> ToMemberBookingDtoAsync(Guid tenantId, BookingDto raw, CancellationToken ct)
    {
        var booking = await _db.ActivityBookings.AsNoTracking()
            .Include(b => b.Session).ThenInclude(s => s!.Activity)
            .Include(b => b.Session).ThenInclude(s => s!.CoachUser)
            .FirstAsync(b => b.Id == raw.Id, ct);

        var dto = ToMemberBookingDto(booking);
        dto.QuotaConsumed = Core.Constants.ActivityBookingStatuses.IsQuotaConsuming(booking.Status);
        if (!booking.MemberId.HasValue)
            return dto;

        var covering = await CoveringMembershipAsync(tenantId, booking.MemberId.Value, ct);
        if (covering != null && dto.QuotaConsumed && booking.CoveringMembershipId == covering.Id)
        {
            dto.QuotaRemaining = await _entitlements.RemainingQuotaAsync(
                tenantId, booking.MemberId.Value, booking.Session!.ActivityId, covering, ct);
        }
        return dto;
    }

    private async Task<Membership?> CoveringMembershipAsync(Guid tenantId, Guid memberId, CancellationToken ct)
    {
        var today = MembershipOperational.TodayCairo();
        var memberships = await _db.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.MemberId == memberId && !m.IsDeleted)
            .ToListAsync(ct);
        return MembershipOperational.SelectCoveringToday(memberships, today);
    }

    /// <summary>
    /// identityUserId is the Identity user id (JWT sub). GymMember.AppUserId points at the
    /// domain AppUser.Id — a separate id linked to Identity only via AppUser.UserId (string) —
    /// so resolution is a two-hop lookup, mirroring SessionBookingService.CheckInBookingAsync's
    /// staff resolution. Comparing identityUserId directly against GymMember.AppUserId (as a
    /// single-hop lookup would) never matches, since those are different id spaces.
    /// </summary>
    private async Task<Guid?> ResolveMemberIdAsync(Guid tenantId, Guid identityUserId, CancellationToken ct)
    {
        var appUserId = await _db.AppUsers.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.UserId == identityUserId.ToString() && !u.IsDeleted)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
        if (appUserId == null)
            return null;

        return await _db.GymMembers.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.AppUserId == appUserId.Value && !m.IsDeleted)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(ct);
    }
}
