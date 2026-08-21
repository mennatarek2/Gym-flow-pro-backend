namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Activities;
using GMS.Application.DTOs.Plans;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Service for membership plan management.
/// Handles CRUD operations with multi-tenancy support.
/// </summary>
public class MembershipPlanService : IMembershipPlanService
{
    private readonly GymFlowProDbContext _dbContext;
    private readonly IRepository<MembershipPlan> _planRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<MembershipPlanService> _logger;

    public MembershipPlanService(
        GymFlowProDbContext dbContext,
        IRepository<MembershipPlan> planRepository,
        ITenantContext tenantContext,
        ILogger<MembershipPlanService> logger)
    {
        _dbContext = dbContext;
        _planRepository = planRepository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<List<PlanListItemDto>>> GetPlansAsync(Guid tenantId)
    {
        try
        {
            var plans = await _dbContext.MembershipPlans
                .Where(p => p.TenantId == tenantId && p.IsActive)
                .OrderByDescending(p => p.CreatedAtUtc)
                .Select(p => new PlanListItemDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    NameAr = p.NameAr,
                    PlanType = p.PlanType,
                    Price = p.Price,
                    Currency = p.Currency,
                    DurationDays = p.DurationDays,
                    IsActive = p.IsActive,
                    CreatedAtUtc = p.CreatedAtUtc
                })
                .ToListAsync();

            return Result<List<PlanListItemDto>>.Success(plans);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving membership plans for tenant {TenantId}", tenantId);
            return Result<List<PlanListItemDto>>.Failure(
                "Failed to retrieve plans / فشل في جلب الخطط",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<PlanDetailDto>> GetPlanByIdAsync(Guid id)
    {
        try
        {
            var plan = await _dbContext.MembershipPlans
                .Include(p => p.Memberships)
                .Include(p => p.Entitlements)
                    .ThenInclude(e => e.Activity)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (plan == null)
                return Result<PlanDetailDto>.Failure(
                    "Membership plan not found / الخطة غير موجودة");

            var activeMemberships = plan.Memberships
                .Count(m => m.Status == "active" || m.Status == "frozen");

            var dto = MapDetail(plan, activeMemberships);

            return Result<PlanDetailDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving membership plan {PlanId}", id);
            return Result<PlanDetailDto>.Failure(
                "Failed to retrieve plan / فشل في جلب الخطة",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<PlanDetailDto>> CreatePlanAsync(Guid tenantId, CreatePlanRequest request)
    {
        try
        {
            // Create new plan entity
            var plan = new MembershipPlan
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = request.Name,
                NameAr = request.NameAr,
                Description = request.Description ?? string.Empty,
                DescriptionAr = request.DescriptionAr ?? string.Empty,
                PlanType = request.PlanType.ToLower(),
                Price = request.Price,
                Currency = "EGP",
                DurationDays = request.DurationDays,
                SessionCount = request.SessionCount,
                TimeRestrictionStart = request.TimeRestrictionStart,
                TimeRestrictionEnd = request.TimeRestrictionEnd,
                InvitationQuota = request.InvitationQuota,
                ReferralInviteQuota = request.ReferralInviteQuota,
                ReferralRewardType = NormalizeRewardType(request.ReferralRewardType),
                ReferralRewardValue = request.ReferralRewardValue,
                TrialVisitLimit = request.TrialVisitLimit,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            // Save to database
            await _planRepository.AddAsync(plan);

            var entitlementError = await ReplaceEntitlementsAsync(tenantId, plan.Id, request.Entitlements, defaultIfNull: true);
            if (entitlementError != null)
                return Result<PlanDetailDto>.Failure(entitlementError);

            _logger.LogInformation(
                "Membership plan created: {PlanId} ({PlanName}) for tenant {TenantId}",
                plan.Id, plan.Name, tenantId);

            // Return the created plan details
            return await GetPlanByIdAsync(plan.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating membership plan for tenant {TenantId}", tenantId);
            return Result<PlanDetailDto>.Failure(
                "Failed to create plan / فشل في إنشاء الخطة",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<PlanDetailDto>> UpdatePlanAsync(Guid id, UpdatePlanRequest request)
    {
        try
        {
            var plan = await _dbContext.MembershipPlans.FirstOrDefaultAsync(p => p.Id == id);
            if (plan == null)
                return Result<PlanDetailDto>.Failure(
                    "Membership plan not found / الخطة غير موجودة");

            // Update plan properties
            plan.Name = request.Name;
            plan.NameAr = request.NameAr;
            plan.Description = request.Description ?? string.Empty;
            plan.DescriptionAr = request.DescriptionAr ?? string.Empty;
            plan.PlanType = request.PlanType.ToLower();
            plan.Price = request.Price;
            plan.DurationDays = request.DurationDays;
            plan.SessionCount = request.SessionCount;
            plan.TimeRestrictionStart = request.TimeRestrictionStart;
            plan.TimeRestrictionEnd = request.TimeRestrictionEnd;
            plan.InvitationQuota = request.InvitationQuota;
            plan.ReferralInviteQuota = request.ReferralInviteQuota;
            plan.ReferralRewardType = NormalizeRewardType(request.ReferralRewardType);
            plan.ReferralRewardValue = request.ReferralRewardValue;
            plan.TrialVisitLimit = request.TrialVisitLimit;
            plan.UpdatedAtUtc = DateTime.UtcNow;

            await _planRepository.UpdateAsync(plan);

            if (request.Entitlements != null)
            {
                var entitlementError = await ReplaceEntitlementsAsync(plan.TenantId, plan.Id, request.Entitlements, defaultIfNull: false);
                if (entitlementError != null)
                    return Result<PlanDetailDto>.Failure(entitlementError);
            }

            _logger.LogInformation(
                "Membership plan updated: {PlanId} ({PlanName})",
                plan.Id, plan.Name);

            // Return the updated plan details
            return await GetPlanByIdAsync(plan.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating membership plan {PlanId}", id);
            return Result<PlanDetailDto>.Failure(
                "Failed to update plan / فشل في تحديث الخطة",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result> DeletePlanAsync(Guid id)
    {
        try
        {
            var plan = await _dbContext.MembershipPlans
                .Include(p => p.Memberships)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (plan == null)
                return Result.Failure("Membership plan not found / الخطة غير موجودة");

            // Check if there are active memberships on this plan
            var activeMemberships = plan.Memberships
                .Where(m => m.Status == "active" || m.Status == "frozen")
                .ToList();

            if (activeMemberships.Any())
                return Result.Failure(
                    $"Cannot delete plan with {activeMemberships.Count} active memberships / لا يمكن حذف خطة بها أعضاء نشطين",
                    $"This plan has {activeMemberships.Count} active members");

            // Soft delete: mark as inactive
            plan.IsActive = false;
            plan.UpdatedAtUtc = DateTime.UtcNow;

            await _planRepository.UpdateAsync(plan);

            _logger.LogInformation(
                "Membership plan deleted (soft): {PlanId} ({PlanName})",
                plan.Id, plan.Name);

            return Result.Success("Plan deleted successfully / تم حذف الخطة بنجاح");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting membership plan {PlanId}", id);
            return Result.Failure(
                "Failed to delete plan / فشل في حذف الخطة",
                ex.Message);
        }
    }

    private static string? NormalizeRewardType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var t = raw.Trim().ToLowerInvariant();
        return t is "credit" or "free_days" ? t : null;
    }

    private static PlanDetailDto MapDetail(MembershipPlan plan, int activeMemberships) => new()
    {
        Id = plan.Id,
        Name = plan.Name,
        NameAr = plan.NameAr,
        Description = plan.Description,
        DescriptionAr = plan.DescriptionAr,
        PlanType = plan.PlanType,
        Price = plan.Price,
        Currency = plan.Currency,
        DurationDays = plan.DurationDays,
        SessionCount = plan.SessionCount,
        TimeRestrictionStart = plan.TimeRestrictionStart,
        TimeRestrictionEnd = plan.TimeRestrictionEnd,
        InvitationQuota = plan.InvitationQuota,
        ReferralInviteQuota = plan.ReferralInviteQuota,
        ReferralRewardType = plan.ReferralRewardType,
        ReferralRewardValue = plan.ReferralRewardValue,
        TrialVisitLimit = plan.TrialVisitLimit,
        IsActive = plan.IsActive,
        ActiveMemberships = activeMemberships,
        TotalMemberships = plan.Memberships.Count,
        Entitlements = plan.Entitlements
            .Where(e => !e.IsDeleted)
            .Select(e => new PlanEntitlementDto
            {
                Id = e.Id,
                ActivityId = e.ActivityId,
                ActivityName = e.Activity?.Name ?? "",
                ActivityKind = e.Activity?.Kind ?? "",
                AccessMode = e.AccessMode,
                QuotaLimit = e.QuotaLimit,
                QuotaPeriod = e.QuotaPeriod
            })
            .ToList(),
        CreatedAtUtc = plan.CreatedAtUtc,
        UpdatedAtUtc = plan.UpdatedAtUtc
    };

    private async Task<string?> ReplaceEntitlementsAsync(
        Guid tenantId, Guid planId, List<UpsertPlanEntitlementRequest>? requested, bool defaultIfNull)
    {
        var floor = await GymFloorBootstrap.EnsureActivityAsync(_dbContext, tenantId);

        List<UpsertPlanEntitlementRequest> items;
        if (requested == null)
        {
            if (!defaultIfNull)
                return null;
            items = new List<UpsertPlanEntitlementRequest>
            {
                new() { ActivityId = floor.Id, AccessMode = EntitlementAccessModes.Included }
            };
        }
        else
        {
            items = requested;
        }

        var seen = new HashSet<Guid>();
        var built = new List<PlanEntitlement>();
        foreach (var item in items)
        {
            if (!seen.Add(item.ActivityId))
                return "Duplicate activity entitlement / صلاحية مكررة";

            var activity = await _dbContext.Activities
                .FirstOrDefaultAsync(a => a.Id == item.ActivityId && a.TenantId == tenantId);
            if (activity == null)
                return "Activity not found / النشاط غير موجود";

            var mode = (item.AccessMode ?? "").Trim().ToLowerInvariant();
            if (!EntitlementAccessModes.All.Contains(mode))
                return "Invalid access mode / وضع الوصول غير صالح";

            string? period = null;
            int? limit = null;
            if (mode == EntitlementAccessModes.Limited)
            {
                if (item.QuotaLimit is not > 0)
                    return "Limited entitlements need a quota / الحصة مطلوبة";
                period = string.IsNullOrWhiteSpace(item.QuotaPeriod)
                    ? EntitlementQuotaPeriods.CairoMonth
                    : item.QuotaPeriod.Trim().ToLowerInvariant();
                if (!EntitlementQuotaPeriods.All.Contains(period))
                    return "Invalid quota period / فترة الحصة غير صالحة";
                limit = item.QuotaLimit;
            }

            built.Add(new PlanEntitlement
            {
                TenantId = tenantId,
                PlanId = planId,
                ActivityId = activity.Id,
                AccessMode = mode,
                QuotaLimit = limit,
                QuotaPeriod = period,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        var existing = await _dbContext.PlanEntitlements
            .Where(e => e.PlanId == planId)
            .ToListAsync();
        _dbContext.PlanEntitlements.RemoveRange(existing);
        _dbContext.PlanEntitlements.AddRange(built);
        await _dbContext.SaveChangesAsync();
        return null;
    }
}
