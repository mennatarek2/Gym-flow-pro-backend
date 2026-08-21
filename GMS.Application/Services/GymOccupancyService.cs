namespace GMS.Application.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Attendance;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Occupancy is a derived view of existing attendance (open visits today).
/// Does not write check-in/out and does not introduce Branch capacity.
/// </summary>
public class GymOccupancyService : IGymOccupancyService
{
    private readonly GymFlowProDbContext _db;
    private readonly ILogger<GymOccupancyService> _logger;

    public GymOccupancyService(GymFlowProDbContext db, ILogger<GymOccupancyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<GymOccupancyDto>> GetOccupancyAsync(Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            var tenant = await _db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
            if (tenant == null)
                return Result<GymOccupancyDto>.Failure("Tenant not found / المنظمة غير موجودة");

            // Must match CheckinService.GetTodayAttendanceAsync (UTC calendar day).
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1);

            var currentlyInside = await _db.GymAttendances
                .CountAsync(a =>
                    a.TenantId == tenantId
                    && a.CheckInAtUtc >= todayStart
                    && a.CheckInAtUtc < todayEnd
                    && a.CheckOutAtUtc == null
                    && a.EntryMethod != "class", ct);

            var max = ReadMaxCapacity(tenant.Settings);
            int? available = null;
            int? percent = null;
            if (max.HasValue)
            {
                available = Math.Max(0, max.Value - currentlyInside);
                percent = (int)Math.Round(currentlyInside * 100d / max.Value, MidpointRounding.AwayFromZero);
            }

            return Result<GymOccupancyDto>.Success(new GymOccupancyDto
            {
                GymName = tenant.Name,
                GymNameAr = tenant.NameAr,
                GymActive = tenant.IsActive,
                MaxCapacity = max,
                CurrentlyInside = currentlyInside,
                Available = available,
                OccupancyPercent = percent,
                Source = "attendance_open_visits"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing occupancy for {TenantId}", tenantId);
            return Result<GymOccupancyDto>.Failure(
                "Failed to retrieve occupancy / فشل في جلب الإشغال", ex.Message);
        }
    }

    internal static int? ReadMaxCapacity(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (!doc.RootElement.TryGetProperty(TenantSettingsKeys.GymMaxCapacity, out var value)
                || value.ValueKind == JsonValueKind.Null)
                return null;
            int n;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out n))
                return n >= 1 ? n : null;
            if (value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), out n))
                return n >= 1 ? n : null;
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
