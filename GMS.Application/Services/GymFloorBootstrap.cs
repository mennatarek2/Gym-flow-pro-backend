namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

public static class GymFloorBootstrap
{
    public static async Task<Activity> EnsureActivityAsync(GymFlowProDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        var existing = await db.Activities
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.SystemKey == ActivitySystemKeys.GymFloor, ct);
        if (existing != null)
            return existing;

        var floor = new Activity
        {
            TenantId = tenantId,
            Name = "Gym floor",
            NameAr = "صالة الجيم",
            Description = "Door check-in access",
            DescriptionAr = "دخول الصالة",
            Kind = ActivityKinds.Facility,
            SystemKey = ActivitySystemKeys.GymFloor,
            IsSystem = true,
            IsActive = true,
            BookingRequired = false,
            VisibleToMembers = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Activities.Add(floor);
        await db.SaveChangesAsync(ct);
        return floor;
    }
}
