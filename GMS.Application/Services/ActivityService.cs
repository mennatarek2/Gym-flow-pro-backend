namespace GMS.Application.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Activities;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

public class ActivityService : IActivityService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly GymFlowProDbContext _db;
    private readonly ILogger<ActivityService> _logger;

    public ActivityService(GymFlowProDbContext db, ILogger<ActivityService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<List<ActivityDto>>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        var rows = await _db.Activities.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderBy(a => a.IsSystem ? 0 : 1)
            .ThenBy(a => a.Name)
            .ToListAsync(ct);
        return Result<List<ActivityDto>>.Success(rows.Select(ToDto).ToList());
    }

    public async Task<Result<ActivityDto>> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var row = await _db.Activities.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId, ct);
        if (row == null)
            return Result<ActivityDto>.Failure("Activity not found / النشاط غير موجود");
        return Result<ActivityDto>.Success(ToDto(row));
    }

    public async Task<Result<ActivityDto>> CreateAsync(Guid tenantId, CreateActivityRequest request, CancellationToken ct = default)
    {
        var name = (request.Name ?? "").Trim();
        if (string.IsNullOrEmpty(name))
            return Result<ActivityDto>.Failure("Name is required / الاسم مطلوب");

        var kind = (request.Kind ?? ActivityKinds.Class).Trim().ToLowerInvariant();
        if (kind != ActivityKinds.Class && kind != ActivityKinds.Facility)
            return Result<ActivityDto>.Failure("Invalid activity kind / نوع النشاط غير صالح");

        var entity = new Activity
        {
            TenantId = tenantId,
            Name = name,
            NameAr = (request.NameAr ?? "").Trim(),
            Description = (request.Description ?? "").Trim(),
            DescriptionAr = (request.DescriptionAr ?? "").Trim(),
            Kind = kind,
            DefaultCapacity = request.DefaultCapacity,
            DefaultDurationMinutes = kind == ActivityKinds.Class ? request.DefaultDurationMinutes : null,
            DropInPrice = kind == ActivityKinds.Class ? request.DropInPrice : null,
            BookingRequired = request.BookingRequired,
            VisibleToMembers = request.VisibleToMembers,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Activities.Add(entity);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Activity {ActivityId} created for tenant {TenantId}", entity.Id, tenantId);
        return Result<ActivityDto>.Success(ToDto(entity));
    }

    public async Task<Result<ActivityDto>> UpdateAsync(Guid tenantId, Guid id, UpdateActivityRequest request, CancellationToken ct = default)
    {
        var entity = await _db.Activities.FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId, ct);
        if (entity == null)
            return Result<ActivityDto>.Failure("Activity not found / النشاط غير موجود");
        if (entity.IsSystem)
            return Result<ActivityDto>.Failure("System activities cannot be edited / لا يمكن تعديل أنشطة النظام");

        var name = (request.Name ?? "").Trim();
        if (string.IsNullOrEmpty(name))
            return Result<ActivityDto>.Failure("Name is required / الاسم مطلوب");

        entity.Name = name;
        entity.NameAr = (request.NameAr ?? "").Trim();
        entity.Description = (request.Description ?? "").Trim();
        entity.DescriptionAr = (request.DescriptionAr ?? "").Trim();
        entity.DefaultCapacity = request.DefaultCapacity;
        entity.DefaultDurationMinutes = entity.Kind == ActivityKinds.Class ? request.DefaultDurationMinutes : null;
        entity.DropInPrice = entity.Kind == ActivityKinds.Class ? request.DropInPrice : null;
        entity.BookingRequired = request.BookingRequired;
        entity.VisibleToMembers = request.VisibleToMembers;
        if (request.IsActive.HasValue)
            entity.IsActive = request.IsActive.Value;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Result<ActivityDto>.Success(ToDto(entity));
    }

    public async Task<Result> DeleteAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Activities.FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId, ct);
        if (entity == null)
            return Result.Failure("Activity not found / النشاط غير موجود");
        if (entity.IsSystem)
            return Result.Failure("System activities cannot be deleted / لا يمكن حذف أنشطة النظام");

        entity.IsDeleted = true;
        entity.IsActive = false;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<List<ActivityScheduleDto>>> ListSchedulesAsync(Guid tenantId, Guid activityId, CancellationToken ct = default)
    {
        var activity = await _db.Activities.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == activityId && a.TenantId == tenantId, ct);
        if (activity == null)
            return Result<List<ActivityScheduleDto>>.Failure("Activity not found / النشاط غير موجود");

        var rows = await _db.ActivitySchedules.AsNoTracking()
            .Include(s => s.CoachUser)
            .Where(s => s.ActivityId == activityId && s.TenantId == tenantId)
            .OrderBy(s => s.StartTime)
            .ToListAsync(ct);

        return Result<List<ActivityScheduleDto>>.Success(rows.Select(ToScheduleDto).ToList());
    }

    public async Task<Result<ActivityScheduleDto>> CreateScheduleAsync(Guid tenantId, Guid activityId, CreateScheduleRequest request, CancellationToken ct = default)
    {
        var activity = await _db.Activities.FirstOrDefaultAsync(a => a.Id == activityId && a.TenantId == tenantId, ct);
        if (activity == null)
            return Result<ActivityScheduleDto>.Failure("Activity not found / النشاط غير موجود");
        if (activity.Kind != ActivityKinds.Class)
            return Result<ActivityScheduleDto>.Failure("Schedules apply to classes only / الجداول للحصص فقط");

        if (request.DaysOfWeek == null || request.DaysOfWeek.Count == 0)
            return Result<ActivityScheduleDto>.Failure("Select at least one day / اختر يوماً واحداً على الأقل");

        if (!TryParseTime(request.StartTime, out var start) || !TryParseTime(request.EndTime, out var end))
            return Result<ActivityScheduleDto>.Failure("Invalid time / الوقت غير صالح");
        if (end <= start)
            return Result<ActivityScheduleDto>.Failure("End time must be after start / وقت الانتهاء بعد البداية");

        var capacity = request.Capacity ?? activity.DefaultCapacity ?? 15;
        if (capacity < 1)
            return Result<ActivityScheduleDto>.Failure("Capacity must be at least 1 / السعة 1 على الأقل");

        Guid? coachAppUserId = null;
        if (request.CoachUserId.HasValue)
        {
            coachAppUserId = await ResolveCoachAppUserIdAsync(tenantId, request.CoachUserId.Value, ct);
            if (!coachAppUserId.HasValue)
                return Result<ActivityScheduleDto>.Failure("Coach not found / المدرب غير موجود");
        }

        var schedule = new ActivitySchedule
        {
            TenantId = tenantId,
            ActivityId = activityId,
            DaysOfWeek = JsonSerializer.Serialize(request.DaysOfWeek, JsonOpts),
            StartTime = start,
            EndTime = end,
            Capacity = capacity,
            CoachUserId = coachAppUserId,
            EffectiveFrom = request.EffectiveFrom ?? DateOnly.FromDateTime(DateTime.UtcNow),
            EffectiveUntil = request.EffectiveUntil,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ActivitySchedules.Add(schedule);
        await _db.SaveChangesAsync(ct);

        if (schedule.CoachUserId.HasValue)
            await _db.Entry(schedule).Reference(s => s.CoachUser).LoadAsync(ct);

        return Result<ActivityScheduleDto>.Success(ToScheduleDto(schedule));
    }

    public async Task<Result> DeleteScheduleAsync(Guid tenantId, Guid scheduleId, CancellationToken ct = default)
    {
        var schedule = await _db.ActivitySchedules.FirstOrDefaultAsync(s => s.Id == scheduleId && s.TenantId == tenantId, ct);
        if (schedule == null)
            return Result.Failure("Schedule not found / الجدول غير موجود");

        schedule.IsDeleted = true;
        schedule.IsActive = false;
        schedule.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static ActivityDto ToDto(Activity a) => new()
    {
        Id = a.Id,
        Name = a.Name,
        NameAr = a.NameAr,
        Description = string.IsNullOrEmpty(a.Description) ? null : a.Description,
        DescriptionAr = string.IsNullOrEmpty(a.DescriptionAr) ? null : a.DescriptionAr,
        Kind = a.Kind,
        SystemKey = a.SystemKey,
        IsSystem = a.IsSystem,
        IsActive = a.IsActive,
        BookingRequired = a.BookingRequired,
        DefaultCapacity = a.DefaultCapacity,
        DefaultDurationMinutes = a.DefaultDurationMinutes,
        DropInPrice = a.DropInPrice,
        VisibleToMembers = a.VisibleToMembers
    };

    private static ActivityScheduleDto ToScheduleDto(ActivitySchedule s) => new()
    {
        Id = s.Id,
        ActivityId = s.ActivityId,
        DaysOfWeek = s.DaysOfWeek,
        StartTime = s.StartTime,
        EndTime = s.EndTime,
        Capacity = s.Capacity,
        CoachUserId = s.CoachUserId,
        CoachName = s.CoachUser == null ? null : $"{s.CoachUser.FirstName} {s.CoachUser.LastName}".Trim(),
        EffectiveFrom = s.EffectiveFrom,
        EffectiveUntil = s.EffectiveUntil,
        IsActive = s.IsActive
    };

    private static bool TryParseTime(string? raw, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var s = raw.Trim();
        if (TimeOnly.TryParse(s, out time))
            return true;
        if (s.Length >= 5 && TimeOnly.TryParse(s[..5], out time))
            return true;
        return false;
    }

    /// <summary>Staff APIs expose Identity user id; schedules FK to app_users.Id via UserId link.</summary>
    private async Task<Guid?> ResolveCoachAppUserIdAsync(Guid tenantId, Guid coachUserId, CancellationToken ct)
    {
        var direct = await _db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == coachUserId && u.TenantId == tenantId, ct);
        if (direct != null)
            return direct.Id;

        var key = coachUserId.ToString();
        var linked = await _db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.UserId == key, ct);
        return linked?.Id;
    }
}
