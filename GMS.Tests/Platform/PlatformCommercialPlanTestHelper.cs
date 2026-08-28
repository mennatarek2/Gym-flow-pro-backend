namespace GMS.Tests.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

public static class PlatformCommercialPlanTestHelper
{
    public static async Task SeedCommercialPlansAsync(PlatformDbContext db)
    {
        if (await db.CommercialPlans.AnyAsync())
            return;

        db.CommercialPlans.AddRange(CommercialPlanSeed.BuildAll());
        await db.SaveChangesAsync();
    }

    public static async Task SeedTierFeatureMapAsync(PlatformDbContext db)
    {
        var expected = TierFeatureMapSeed.BuildAll();
        var existing = await db.TierFeatureMaps
            .Select(m => m.Tier + "|" + m.FeatureKey)
            .ToListAsync();

        var set = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = expected.Where(r => !set.Contains($"{r.Tier}|{r.FeatureKey}")).ToList();
        if (missing.Count == 0)
            return;

        db.TierFeatureMaps.AddRange(missing);
        await db.SaveChangesAsync();
    }

    public static ICommercialPlanService CreatePlanService(PlatformDbContext db, IPlatformAuditService? audit = null) =>
        new CommercialPlanService(
            db,
            audit ?? new NoOpPlatformAudit(),
            NullLogger<CommercialPlanService>.Instance);

    public sealed class NoOpPlatformAudit : IPlatformAuditService
    {
        public Task LogAsync(
            Guid actorPlatformUserId,
            string action,
            Guid? tenantId = null,
            object? before = null,
            object? after = null,
            string? ipAddress = null) => Task.CompletedTask;

        public Task<GMS.Platform.DTOs.PlatformPagedResult<GMS.Platform.DTOs.PlatformAuditLogDto>> ListAsync(
            Guid? tenantId,
            string? action,
            DateOnly? from,
            DateOnly? to,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GMS.Platform.DTOs.PlatformPagedResult<GMS.Platform.DTOs.PlatformAuditLogDto>
            {
                Page = page,
                PageSize = pageSize
            });
    }
}
