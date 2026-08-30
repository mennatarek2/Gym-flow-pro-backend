namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Attendance;
using GMS.Application.DTOs.Notifications;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Check-in service implementing the validation gauntlet for QR and manual check-ins.
/// 
/// PERFORMANCE: Active membership lookups are cached in IMemoryCache with 5-min TTL.
/// Target: QR check-in completes in under 300ms with warm cache.
/// 
/// VALIDATION ORDER (exact sequence — do NOT reorder):
///   1. Gym code → tenant exists
///   2. Member JWT → belongs to this tenant
///   3. Active membership exists
///   4. Membership not frozen
///   5. Time restriction check (for time-limited plans)
///   6. Session count check (for session-pack plans)
///   7. Write attendance record
///   8. Decrement sessions_remaining (atomic)
/// </summary>
public class CheckinService : ICheckinService
{
    private readonly GymFlowProDbContext _dbContext;
    private readonly IMemberRepository _memberRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly IMemoryCache _cache;
    private readonly ICheckinNotifier _notifier;
    private readonly IAuditService _auditService;
    private readonly IStaffNotificationPublisher? _staffNotifications;
    private readonly ILogger<CheckinService> _logger;

    private static readonly TimeSpan MembershipCacheTtl = TimeSpan.FromMinutes(5);

    public CheckinService(
        GymFlowProDbContext dbContext,
        IMemberRepository memberRepo,
        IAttendanceRepository attendanceRepo,
        IMemoryCache cache,
        ICheckinNotifier notifier,
        IAuditService auditService,
        ILogger<CheckinService> logger,
        IStaffNotificationPublisher? staffNotifications = null)
    {
        _dbContext = dbContext;
        _memberRepo = memberRepo;
        _attendanceRepo = attendanceRepo;
        _cache = cache;
        _notifier = notifier;
        _auditService = auditService;
        _logger = logger;
        _staffNotifications = staffNotifications;
    }

    public async Task<Result<QrCheckinResponse>> ProcessQrCheckinAsync(
        QrCheckinRequest request, Guid userId, Guid tenantId)
    {
        // === STEP 1: Gym code → tenant exists ===
        var tenant = await GetTenantByGymCodeAsync(request.GymCode);
        if (tenant == null)
            return Fail<QrCheckinResponse>(
                "Invalid gym QR code / رمز QR غير صالح");

        if (tenant.Id != tenantId)
            return Fail<QrCheckinResponse>(
                "This QR code belongs to a different gym / رمز QR هذا يخص صالة رياضية أخرى");

        // === STEP 2: Member belongs to this tenant ===
        // JWT "sub" is AspNetUsers.Id; gym_members.AppUserId FK points to app_users.Id, and app_users.UserId stores the Identity id string.
        var member = await FindMemberByUserIdAsync(userId, tenantId);
        if (member == null)
            return Fail<QrCheckinResponse>(
                "Please log in to your gym account / يرجى تسجيل الدخول إلى حسابك");

        // === Run shared validation gauntlet ===
        var (membership, error) = await ValidateMembershipGauntletAsync(member, tenantId);
        if (error != null)
            return Fail<QrCheckinResponse>(error);

        // === STEP 7: Write attendance record ===
        var attendance = new GymAttendance
        {
            TenantId = tenantId,
            MemberId = member.Id,
            MembershipId = membership!.Id,
            CheckInAtUtc = DateTime.UtcNow,
            EntryMethod = "qr"
        };

        await _attendanceRepo.CreateCheckinAsync(attendance);

        // === STEP 8: Decrement sessions for session-pack plans ===
        int? sessionsRemaining = await DecrementSessionsIfNeededAsync(membership);

        // Invalidate membership cache after session decrement
        InvalidateMembershipCache(tenantId, member.Id);

        // REM-F7: surface concurrent exhaustion explicitly instead of reporting success.
        if (sessionsRemaining == SessionDecrementFailed)
            return Fail<QrCheckinResponse>(
                "No sessions remaining / لا توجد جلسات متبقية");

        _logger.LogInformation(
            "QR check-in: Member {MemberNumber} at tenant {GymCode}",
            member.MemberNumber, request.GymCode);

        // Push real-time event to dashboard (non-blocking)
        _ = _notifier.NotifyCheckinAsync(
            tenantId, member.Id, member.FullName,
            member.MemberNumber, attendance.CheckInAtUtc, "qr");

        return Result<QrCheckinResponse>.Success(new QrCheckinResponse
        {
            AttendanceId = attendance.Id,
            MemberName = member.FullName,
            MemberNameAr = member.FullNameAr,
            CheckInAtUtc = attendance.CheckInAtUtc,
            PlanName = membership.Plan?.Name ?? string.Empty,
            PlanNameAr = membership.Plan?.NameAr ?? string.Empty,
            SessionsRemaining = sessionsRemaining,
            Message = "Welcome! Check-in successful",
            MessageAr = "أهلاً! تم تسجيل الدخول بنجاح"
        });
    }

    public async Task<Result<ManualCheckinResponse>> ProcessManualCheckinAsync(
        ManualCheckinRequest request, Guid staffUserId, Guid tenantId)
    {
        // CRITICAL: Validate staff user EXISTS and belongs to this tenant BEFORE using it.
        // NOTE: JWT sub = ApplicationUser.Id (Identity PK).
        //       AppUser.UserId stores that same Identity ID as a string.
        //       AppUser.Id is a separate domain PK (NEWSEQUENTIALID) — do NOT compare against it.
        var staffUserIdStr = staffUserId.ToString();
        var staffUser = await _dbContext.AppUsers
            .FirstOrDefaultAsync(u => u.UserId == staffUserIdStr && u.TenantId == tenantId);

        if (staffUser == null)
        {
            _logger.LogWarning(
                "Manual check-in attempt with invalid staff user. StaffUserId: {StaffUserId}, TenantId: {TenantId}",
                staffUserId, tenantId);
            return Fail<ManualCheckinResponse>(
                "Unauthorized: Staff user not found or does not belong to this tenant");
        }

        // Get the member
        var member = await _memberRepo.GetByIdWithMembershipAsync(request.MemberId);
        if (member == null || member.TenantId != tenantId)
            return Fail<ManualCheckinResponse>(
                "Member not found / العضو غير موجود");

        // === Run shared validation gauntlet ===
        var (membership, error) = await ValidateMembershipGauntletAsync(member, tenantId);
        if (error != null)
            return Fail<ManualCheckinResponse>(error);

        // Write attendance record with manual entry
        var reasonText = request.Reason.ToString();
        if (request.Reason == Core.Enums.ManualCheckinReason.Other && !string.IsNullOrWhiteSpace(request.Notes))
            reasonText = $"Other: {request.Notes}";

        var attendance = new GymAttendance
        {
            TenantId = tenantId,
            MemberId = member.Id,
            MembershipId = membership!.Id,
            CheckInAtUtc = DateTime.UtcNow,
            EntryMethod = "manual",
            StaffUserId = staffUser.Id,  // AppUser.Id (domain PK), NOT the JWT sub (ApplicationUser.Id)
            ManualReason = reasonText
        };

        await _attendanceRepo.CreateCheckinAsync(attendance);

        await _auditService.LogAsync("checkin.manual", "GymAttendance", attendance.Id);

        // Decrement sessions for session-pack plans
        int? sessionsRemaining = await DecrementSessionsIfNeededAsync(membership);
        InvalidateMembershipCache(tenantId, member.Id);

        // REM-F7: surface concurrent exhaustion explicitly instead of reporting success.
        if (sessionsRemaining == SessionDecrementFailed)
            return Fail<ManualCheckinResponse>(
                "No sessions remaining / لا توجد جلسات متبقية");

        _logger.LogInformation(
            "Manual check-in: Member {MemberNumber} by staff {StaffId} at tenant {TenantId}",
            member.MemberNumber, staffUserId, tenantId);

        // Push real-time event to dashboard (non-blocking)
        _ = _notifier.NotifyCheckinAsync(
            tenantId, member.Id, member.FullName,
            member.MemberNumber, attendance.CheckInAtUtc, "manual");

        return Result<ManualCheckinResponse>.Success(new ManualCheckinResponse
        {
            AttendanceId = attendance.Id,
            MemberName = member.FullName,
            MemberNameAr = member.FullNameAr,
            CheckInAtUtc = attendance.CheckInAtUtc,
            PlanName = membership.Plan?.Name ?? string.Empty,
            PlanNameAr = membership.Plan?.NameAr ?? string.Empty,
            SessionsRemaining = sessionsRemaining,
            StaffName = staffUser != null ? $"{staffUser.FirstName} {staffUser.LastName}" : "Staff",
            Message = "Manual check-in successful",
            MessageAr = "تم تسجيل الدخول اليدوي بنجاح"
        });
    }

    public async Task<Result<ManualCheckinResponse>> ProcessBarcodeCheckinAsync(
        BarcodeCheckinRequest request, Guid staffUserId, Guid tenantId)
    {
        var staffUserIdStr = staffUserId.ToString();
        var staffUser = await _dbContext.AppUsers
            .FirstOrDefaultAsync(u => u.UserId == staffUserIdStr && u.TenantId == tenantId);

        if (staffUser == null)
        {
            _logger.LogWarning(
                "Barcode check-in attempt with invalid staff user. StaffUserId: {StaffUserId}, TenantId: {TenantId}",
                staffUserId, tenantId);
            return Fail<ManualCheckinResponse>(
                "Unauthorized: Staff user not found or does not belong to this tenant");
        }

        var code = (request.Code ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(code))
            return Fail<ManualCheckinResponse>("Scan a member card / امسح كارنيه العضو");

        // Exact match only (MAC-P0 / C10) — never Contains on barcode path.
        var byNumber = await _memberRepo.GetByMemberNumberAsync(code, tenantId);
        if (byNumber == null)
            return Fail<ManualCheckinResponse>("Member not found / العضو غير موجود");

        var member = await _memberRepo.GetByIdWithMembershipAsync(byNumber.Id);
        if (member == null || member.TenantId != tenantId)
            return Fail<ManualCheckinResponse>("Member not found / العضو غير موجود");

        var (membership, error) = await ValidateMembershipGauntletAsync(member, tenantId);
        if (error != null)
            return Fail<ManualCheckinResponse>(error);

        var attendance = new GymAttendance
        {
            TenantId = tenantId,
            MemberId = member.Id,
            MembershipId = membership!.Id,
            CheckInAtUtc = DateTime.UtcNow,
            EntryMethod = "barcode",
            StaffUserId = staffUser.Id,
            ManualReason = null
        };

        await _attendanceRepo.CreateCheckinAsync(attendance);
        await _auditService.LogAsync("checkin.barcode", "GymAttendance", attendance.Id);

        int? sessionsRemaining = await DecrementSessionsIfNeededAsync(membership);
        InvalidateMembershipCache(tenantId, member.Id);

        // REM-F7: surface concurrent exhaustion explicitly instead of reporting success.
        if (sessionsRemaining == SessionDecrementFailed)
            return Fail<ManualCheckinResponse>(
                "No sessions remaining / لا توجد جلسات متبقية");

        _logger.LogInformation(
            "Barcode check-in: Member {MemberNumber} by staff {StaffId} at tenant {TenantId}",
            member.MemberNumber, staffUserId, tenantId);

        _ = _notifier.NotifyCheckinAsync(
            tenantId, member.Id, member.FullName,
            member.MemberNumber, attendance.CheckInAtUtc, "barcode");

        return Result<ManualCheckinResponse>.Success(new ManualCheckinResponse
        {
            AttendanceId = attendance.Id,
            MemberName = member.FullName,
            MemberNameAr = member.FullNameAr,
            CheckInAtUtc = attendance.CheckInAtUtc,
            PlanName = membership.Plan?.Name ?? string.Empty,
            PlanNameAr = membership.Plan?.NameAr ?? string.Empty,
            SessionsRemaining = sessionsRemaining,
            StaffName = $"{staffUser.FirstName} {staffUser.LastName}",
            Message = "Barcode check-in successful",
            MessageAr = "تم تسجيل الدخول بالباركود بنجاح"
        });
    }

    public async Task<Result<List<MemberSearchResult>>> SearchMembersForCheckinAsync(
        MemberSearchRequest request, Guid tenantId)
    {
        var members = await _memberRepo.SearchAsync(
            request.Query, tenantId, request.IncludeInactive);

        var results = members.Select(m =>
        {
            var operational = MembershipOperational.SelectOperational(m.Memberships);
            var status = operational != null
                ? MembershipOperational.GetEffectiveStatus(operational)
                : "none";
            var isSelectable = m.IsActive
                && operational != null
                && MembershipOperational.IsCheckinEligible(operational);

            string? unselectableReason = null;
            string? unselectableReasonAr = null;

            if (!m.IsActive)
            {
                unselectableReason = "Member account is inactive";
                unselectableReasonAr = "حساب العضو غير نشط";
            }
            else if (status == "expired")
            {
                unselectableReason = "Membership has expired";
                unselectableReasonAr = "انتهت صلاحية العضوية";
            }
            else if (status == "frozen")
            {
                unselectableReason = "Membership is currently frozen";
                unselectableReasonAr = "العضوية مجمدة حالياً";
            }
            else if (status == "cancelled")
            {
                unselectableReason = "Membership was cancelled";
                unselectableReasonAr = "تم إلغاء العضوية";
            }
            else if (status == "scheduled")
            {
                unselectableReason = "Membership has not started yet";
                unselectableReasonAr = "العضوية لم تبدأ بعد";
            }
            else if (status == "pending")
            {
                unselectableReason = "Membership payment is pending";
                unselectableReasonAr = "دفع العضوية معلّق";
            }
            else if (status == "none" || operational == null)
            {
                unselectableReason = "No active membership";
                unselectableReasonAr = "لا توجد عضوية نشطة";
            }
            else if (!isSelectable)
            {
                unselectableReason = "Not eligible for check-in";
                unselectableReasonAr = "غير مؤهل لتسجيل الدخول";
            }

            return new MemberSearchResult
            {
                Id = m.Id,
                MemberNumber = m.MemberNumber,
                FullName = m.FullName,
                FullNameAr = m.FullNameAr,
                PhoneNumber = m.PhoneNumber,
                ProfilePhotoUrl = m.ProfilePhotoUrl,
                MembershipStatus = status,
                PlanName = operational?.Plan?.Name ?? string.Empty,
                PlanNameAr = operational?.Plan?.NameAr ?? string.Empty,
                PlanType = operational?.Plan?.PlanType ?? string.Empty,
                SessionsRemaining = operational?.SessionsRemaining,
                IsSelectable = isSelectable,
                UnselectableReason = isSelectable ? null : unselectableReason,
                UnselectableReasonAr = isSelectable ? null : unselectableReasonAr
            };
        }).ToList();

        return Result<List<MemberSearchResult>>.Success(results);
    }

    // ========================================================================
    // PRIVATE HELPERS
    // ========================================================================

    /// <summary>
    /// Shared validation gauntlet for both QR and manual check-in.
    /// Steps 3-6 from the spec.
    /// Returns (membership, null) on success or (null, errorMessage) on failure.
    /// </summary>
    private async Task<(Membership? membership, string? error)> ValidateMembershipGauntletAsync(
        GymMember member, Guid tenantId)
    {
        // === STEP 3: Active membership exists ===
        var membership = await GetActiveMembershipCachedAsync(member.Id, tenantId);
        if (membership == null)
        {
            await NotifyExpiredMembershipCheckinAsync(tenantId, member);
            return (null, "انتهت صلاحية عضويتك / Your membership has expired");
        }

        // === STEP 4: Freeze check ===
        if (membership.Status == "frozen")
            return (null, "عضويتك مجمدة حالياً / Your membership is currently frozen");

        if (membership.Status != "active")
        {
            await NotifyExpiredMembershipCheckinAsync(tenantId, member);
            return (null, "انتهت صلاحية عضويتك / Your membership has expired");
        }

        // Check if membership date range is still valid (Cairo business day)
        var today = MembershipOperational.TodayCairo();
        if (today < membership.StartDate)
            return (null, "عضويتك لم تبدأ بعد / Your membership has not started yet");

        if (today > membership.EndDate)
        {
            await NotifyExpiredMembershipCheckinAsync(tenantId, member);
            if (membership.Plan?.PlanType == "trial")
                return (null, "TRIAL_EXPIRED_JOIN_OFFER / انتهت تجربتك المجانية — انضم الآن");

            return (null, "انتهت صلاحية عضويتك / Your membership has expired");
        }

        // === STEP 5: Time restriction check (time-limited plans) ===
        if (membership.Plan != null &&
            membership.Plan.TimeRestrictionStart.HasValue &&
            membership.Plan.TimeRestrictionEnd.HasValue)
        {
            var nowTime = TimeOnly.FromDateTime(DateTime.UtcNow);
            var start = membership.Plan.TimeRestrictionStart.Value;
            var end = membership.Plan.TimeRestrictionEnd.Value;

            if (nowTime < start || nowTime > end)
            {
                var timeMsg = $"Morning pass: access {start:hh\\:mm tt}–{end:hh\\:mm tt} only";
                var timeMsgAr = $"باقة صباحية: الدخول من {start:hh\\:mm} إلى {end:hh\\:mm} فقط";
                return (null, $"{timeMsgAr} / {timeMsg}");
            }
        }

        // === STEP 6: Session count check (session-pack plans) ===
        if (membership.Plan?.PlanType == "session_pack")
        {
            if (!membership.SessionsRemaining.HasValue || membership.SessionsRemaining.Value <= 0)
                return (null, "لا توجد جلسات متبقية / No sessions remaining");
        }

        // === Trial visit-limit check (trial plans with a visit cap) ===
        if (membership.Plan?.PlanType == "trial" && membership.Plan.TrialVisitLimit.HasValue)
        {
            var visitCount = await _dbContext.GymAttendances
                .CountAsync(a => a.MemberId == member.Id
                               && a.TenantId == tenantId
                               && a.CheckInAtUtc >= membership.StartDate.ToDateTime(TimeOnly.MinValue));

            if (visitCount >= membership.Plan.TrialVisitLimit.Value)
                return (null, "TRIAL_VISITS_EXHAUSTED / لقد استنفدت جلساتك التجريبية");
        }

        // === Duplicate check-in prevention ===
        var alreadyCheckedIn = await _dbContext.GymAttendances
            .AnyAsync(a => a.MemberId == member.Id
                        && a.TenantId == tenantId
                        && a.CheckInAtUtc >= DateTime.UtcNow.Date);

        if (alreadyCheckedIn)
            return (null, "لقد سجلت دخولك اليوم بالفعل / You have already checked in today");

        return (membership, null);
    }

    /// <summary>
    /// Gets active membership with plan via IMemoryCache.
    /// Cache key: "membership:{tenantId}:{memberId}", TTL: 5 min.
    /// </summary>
    private async Task<Membership?> GetActiveMembershipCachedAsync(Guid memberId, Guid tenantId)
    {
        var cacheKey = $"membership:{tenantId}:{memberId}";

        if (_cache.TryGetValue(cacheKey, out Membership? cached))
            return cached;

        var today = MembershipOperational.TodayCairo();
        var membership = await _dbContext.Memberships
            .Include(ms => ms.Plan)
            .Where(ms => ms.MemberId == memberId
                      && ms.TenantId == tenantId
                      && (ms.Status == "active" || ms.Status == "frozen")
                      && ms.StartDate <= today
                      && ms.EndDate >= today)
            .OrderByDescending(ms => ms.EndDate)
            .ThenByDescending(ms => ms.StartDate)
            .FirstOrDefaultAsync();

        if (membership != null)
        {
            _cache.Set(cacheKey, membership, new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(MembershipCacheTtl));
        }

        return membership;
    }

    /// <summary>
    /// Finds the <see cref="GymMember"/> for this Identity user within the tenant.
    /// Resolves via <c>app_users.UserId</c> (string), which matches the JWT <c>sub</c> claim (Identity user id).
    /// </summary>
    private async Task<GymMember?> FindMemberByUserIdAsync(Guid applicationUserId, Guid tenantId)
    {
        var identityId = applicationUserId.ToString();

        return await _dbContext.GymMembers
            .Include(m => m.Memberships.Where(ms => ms.Status == "active" || ms.Status == "frozen"))
                .ThenInclude(ms => ms.Plan)
            .FirstOrDefaultAsync(m =>
                m.TenantId == tenantId
                && m.AppUser != null
                && m.AppUser.UserId == identityId);
    }

    /// <summary>
    /// Atomically decrements SessionsRemaining for session-pack plans.
    /// Returns the new sessions remaining count, or null if not a session-pack.
    /// Returns -1 when the guarded decrement affected no rows (concurrent exhaustion) so the
    /// caller can surface an explicit error instead of silently keeping attendance without
    /// consumption (REM-F7).
    /// </summary>
    private async Task<int?> DecrementSessionsIfNeededAsync(Membership membership)
    {
        if (membership.Plan == null && membership.PlanId != Guid.Empty)
            await _dbContext.Entry(membership).Reference(m => m.Plan).LoadAsync();

        if (membership.Plan?.PlanType != "session_pack" || !membership.SessionsRemaining.HasValue)
            return membership.Plan?.PlanType == "session_pack" ? membership.SessionsRemaining : null;

        // Atomic decrement via raw SQL to avoid concurrency issues
        var affected = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE memberships SET SessionsRemaining = SessionsRemaining - 1 WHERE Id = {membership.Id} AND SessionsRemaining > 0");

        if (affected == 0)
        {
            _logger.LogWarning(
                "Session decrement affected 0 rows for membership {MembershipId} — consumed concurrently. " +
                "Attendance was recorded but no session was consumed.",
                membership.Id);
            return SessionDecrementFailed;
        }

        // Reload to get the new value
        await _dbContext.Entry(membership).ReloadAsync();

        return membership.SessionsRemaining;
    }

    /// <summary>Sentinel returned when the atomic session decrement could not consume a session.</summary>
    internal const int SessionDecrementFailed = -1;

    private void InvalidateMembershipCache(Guid tenantId, Guid memberId)
    {
        _cache.Remove($"membership:{tenantId}:{memberId}");
    }

    private async Task<Tenant?> GetTenantByGymCodeAsync(string gymCode)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.GymCode == gymCode && !t.IsDeleted);
    }

    private static Result<T> Fail<T>(string error)
        => Result<T>.Failure(error);

    public async Task<Result<List<TodayAttendanceDto>>> GetTodayAttendanceAsync(Guid tenantId, string filter = "all")
    {
        var todayStart = DateTime.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1);

        var query = _dbContext.GymAttendances
            .Include(a => a.Member)
            .Include(a => a.Membership)
                .ThenInclude(ms => ms!.Plan)
            .Where(a => a.TenantId == tenantId
                && a.CheckInAtUtc >= todayStart
                && a.CheckInAtUtc < todayEnd);

        // Filter by entry method
        if (filter.Equals("qr", StringComparison.OrdinalIgnoreCase))
            query = query.Where(a => a.EntryMethod == "qr");
        else if (filter.Equals("manual", StringComparison.OrdinalIgnoreCase))
            query = query.Where(a => a.EntryMethod == "manual");
        else if (filter.Equals("barcode", StringComparison.OrdinalIgnoreCase))
            query = query.Where(a => a.EntryMethod == "barcode");

        var records = await query
            .OrderByDescending(a => a.CheckInAtUtc)
            .Select(a => new TodayAttendanceDto
            {
                Id = a.Id,
                MemberId = a.MemberId,
                MemberNumber = a.Member != null ? a.Member.MemberNumber : "",
                MemberName = a.Member != null ? a.Member.FullName : (a.GuestName ?? "Guest walk-in"),
                MemberNameAr = a.Member != null ? a.Member.FullNameAr : "",
                GuestName = a.GuestName,
                GuestPhone = a.GuestPhone,
                CheckInAtUtc = a.CheckInAtUtc,
                CheckOutAtUtc = a.CheckOutAtUtc,
                EntryMethod = a.EntryMethod,
                PlanName = a.Membership != null && a.Membership.Plan != null
                    ? a.Membership.Plan.Name : null
            })
            .ToListAsync();

        return Result<List<TodayAttendanceDto>>.Success(records);
    }

    private async Task NotifyExpiredMembershipCheckinAsync(Guid tenantId, GymMember member)
    {
        if (_staffNotifications == null) return;
        var dayKey = MembershipOperational.TodayCairo().ToString("yyyyMMdd");
        await _staffNotifications.TryPublishAsync(tenantId, new CreateStaffNotificationRequest
        {
            Type = StaffNotificationTypes.ExpiredMembershipCheckin,
            Category = StaffNotificationCategories.Attendance,
            Priority = StaffNotificationPriorities.Critical,
            Title = "Expired membership check-in attempt",
            TitleAr = "محاولة حضور بعضوية منتهية",
            Body = $"{member.FullName} tried to check in with an expired membership.",
            BodyAr = $"حاول {member.FullName} الحضور بعضوية منتهية.",
            EntityType = "GymMember",
            EntityId = member.Id,
            ActionUrl = $"/dashboard/members/{member.Id}/",
            DedupeKey = $"expired-checkin:{dayKey}:{member.Id:N}",
            RecipientRoles = new[] { "Owner", "Manager", "Receptionist" }
        });
    }
}
