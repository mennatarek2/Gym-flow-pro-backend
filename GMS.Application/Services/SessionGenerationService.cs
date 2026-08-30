namespace GMS.Application.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Application.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Bounded, idempotent generation of <see cref="ActivitySession"/> rows from active
/// <see cref="ActivitySchedule"/> rows over a rolling window (default next 30 days).
/// Uniqueness (TenantId, ScheduleId, StartsAtUtc) prevents duplicates across runs.
/// </summary>
public class SessionGenerationService : ISessionGenerationService
{
    private const string CairoTimeZone = "Egypt Standard Time";
    private static readonly TimeZoneInfo CairoTz = TimeZoneInfo.FindSystemTimeZoneById(CairoTimeZone);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public const int DefaultWindowDays = 30;

    private readonly GymFlowProDbContext _db;
    private readonly ILogger<SessionGenerationService> _logger;

    public SessionGenerationService(GymFlowProDbContext db, ILogger<SessionGenerationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> GenerateUpcomingSessionsAsync(Guid tenantId, int? windowDaysOverride = null, CancellationToken ct = default)
    {
        var days = ResolveWindowDays(tenantId, windowDaysOverride);
        if (days <= 0)
            return 0;

        // Cairo day boundaries — schedules are local times.
        var todayCairo = MembershipOperational.TodayCairo();
        var windowStartLocal = todayCairo.ToDateTime(TimeOnly.MinValue);
        var windowEndLocal = todayCairo.AddDays(days).ToDateTime(TimeOnly.MinValue);

        var schedules = await _db.ActivitySchedules.AsNoTracking()
            .Include(s => s.Activity)
            .Where(s => s.TenantId == tenantId && s.IsActive && !s.IsDeleted
                        && s.Activity != null && s.Activity.IsActive && !s.Activity.IsDeleted)
            .ToListAsync(ct);

        // Existing sessions in the window — the idempotency guard.
        var existingKeys = await _db.ActivitySessions.AsNoTracking()
            .Where(s => s.TenantId == tenantId
                        && s.ScheduleId != null
                        && s.StartsAtUtc >= ToUtc(windowStartLocal)
                        && s.StartsAtUtc < ToUtc(windowEndLocal))
            .Select(s => new { s.ScheduleId, s.StartsAtUtc })
            .ToListAsync(ct);
        var existing = new HashSet<(Guid ScheduleId, DateTime StartsAt)>(
            existingKeys.Select(k => (k.ScheduleId!.Value, k.StartsAtUtc)));

        var created = 0;
        foreach (var schedule in schedules)
        {
            var daySet = ParseDays(schedule.DaysOfWeek);
            if (daySet.Count == 0)
                continue;

            for (var date = windowStartLocal; date < windowEndLocal; date = date.AddDays(1))
            {
                if (!daySet.Contains(date.DayOfWeek))
                    continue;
                if (date < schedule.EffectiveFrom.ToDateTime(TimeOnly.MinValue))
                    continue;
                if (schedule.EffectiveUntil.HasValue && date > schedule.EffectiveUntil.Value.ToDateTime(TimeOnly.MinValue))
                    continue;

                var startsLocal = date.Add(schedule.StartTime.ToTimeSpan());
                var startsUtc = ToUtc(startsLocal);
                if (startsUtc < DateTime.UtcNow)
                    continue; // never regenerate the past

                var key = (schedule.Id, startsUtc);
                if (existing.Contains(key))
                    continue;

                _db.ActivitySessions.Add(new ActivitySession
                {
                    TenantId = tenantId,
                    ActivityId = schedule.ActivityId,
                    ScheduleId = schedule.Id,
                    StartsAtUtc = startsUtc,
                    EndsAtUtc = ToUtc(startsLocal.AddMinutes(Math.Max(1, MinutesBetween(schedule.StartTime, schedule.EndTime)))),
                    Capacity = schedule.Capacity,
                    CoachUserId = schedule.CoachUserId,
                    Status = ActivitySessionStatuses.Upcoming,
                    CreatedAtUtc = DateTime.UtcNow
                });
                existing.Add(key);
                created++;
            }
        }

        if (created > 0)
            await _db.SaveChangesAsync(ct);

        if (created > 0 || _logger.IsEnabled(LogLevel.Debug))
            _logger.LogInformation("SessionGeneration: tenant {TenantId} created {Count} sessions for next {Days}d",
                tenantId, created, days);
        return created;
    }

    /// <summary>Marks past sessions completed and no-shows stale booked members. Returns affected booking count.</summary>
    public async Task<int> FinalizeElapsedSessionsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var elapsed = await _db.ActivitySessions
            .Include(s => s.Bookings)
            .Where(s => s.TenantId == tenantId
                        && s.Status == ActivitySessionStatuses.Upcoming
                        && s.EndsAtUtc < now)
            .ToListAsync(ct);

        var changedBookings = 0;
        foreach (var session in elapsed)
        {
            session.Status = ActivitySessionStatuses.Completed;
            session.UpdatedAtUtc = now;

            foreach (var booking in session.Bookings.Where(b => !b.IsDeleted && b.Status == ActivityBookingStatuses.Booked))
            {
                booking.Status = ActivityBookingStatuses.NoShow;
                booking.UpdatedAtUtc = now;
                changedBookings++;
            }
        }

        if (changedBookings > 0 || elapsed.Count > 0)
            await _db.SaveChangesAsync(ct);
        return changedBookings;
    }

    /// <summary>Tenant-configurable window (session_generation_days), clamped to [1, 60]; default 30.</summary>
    private int ResolveWindowDays(Guid tenantId, int? overrideDays)
    {
        if (overrideDays.HasValue)
            return Math.Clamp(overrideDays.Value, 1, 60);

        try
        {
            var raw = _db.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => t.Settings)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty(TenantSettingsKeys.SessionGenerationDays, out var el)
                    && el.TryGetInt32(out var v))
                    return Math.Clamp(v, 1, 60);
            }
        }
        catch (JsonException)
        {
            // malformed settings — fall through to default
        }
        return DefaultWindowDays;
    }

    internal static HashSet<DayOfWeek> ParseDays(string? json)
    {
        var result = new HashSet<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(json))
            return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
                {
                    // Accept both ISO (0=Sunday..6=Saturday) and C# enum (1=Sunday..7=Saturday) conventions.
                    if (n is >= 0 and <= 6)
                        result.Add((DayOfWeek)n);
                    else if (n is >= 1 and <= 7 && n == 7)
                        result.Add(DayOfWeek.Sunday);
                }
                else if (el.ValueKind == JsonValueKind.String &&
                         Enum.TryParse<DayOfWeek>(el.GetString(), ignoreCase: true, out var dow))
                {
                    result.Add(dow);
                }
            }
        }
        catch (JsonException)
        {
            // unparseable → empty set → schedule generates nothing (safe default)
        }
        return result;
    }

    internal static int MinutesBetween(TimeOnly start, TimeOnly end)
    {
        var span = end - start;
        return span <= TimeSpan.Zero ? 60 : (int)span.TotalMinutes;
    }

    internal static DateTime ToUtc(DateTime cairoLocal) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(cairoLocal, DateTimeKind.Unspecified), CairoTz);
}
