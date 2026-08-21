namespace GMS.Application.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Dual-sided referral rewards: hold → grant credit/free_days; forfeit/reverse on refund.
/// </summary>
public class ReferralRewardService : IReferralRewardService
{
    private readonly GymFlowProDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly IWhatsAppService _whatsApp;
    private readonly ILogger<ReferralRewardService> _logger;

    public ReferralRewardService(
        GymFlowProDbContext dbContext,
        IAuditService auditService,
        IWhatsAppService whatsApp,
        ILogger<ReferralRewardService> logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _whatsApp = whatsApp;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task CreateHoldsForConvertedInvitationAsync(
        Guid tenantId,
        Guid invitationId,
        Guid? saleId,
        decimal saleAmount,
        CancellationToken ct = default)
    {
        var invite = await _dbContext.MemberInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.TenantId == tenantId, ct);
        if (invite == null || invite.Status != "converted" || !invite.ConvertedMemberId.HasValue)
            return;

        var existing = await _dbContext.ReferralRewards
            .AnyAsync(r => r.TenantId == tenantId && r.InvitationId == invitationId, ct);
        if (existing)
            return;

        var tenant = await _dbContext.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        var settings = tenant?.Settings;

        var monthlyCap = GetSettingInt(settings, TenantSettingsKeys.ReferralMonthlyRewardedCap, 10);
        var holdDays = GetSettingInt(settings, TenantSettingsKeys.ReferralHoldDays, 14);

        var cairoTz = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
        var today = MembershipOperational.TodayCairo();
        var monthStartLocal = new DateOnly(today.Year, today.Month, 1).ToDateTime(TimeOnly.MinValue);
        var monthStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(monthStartLocal, DateTimeKind.Unspecified), cairoTz);

        var rewardedThisMonth = await (
            from r in _dbContext.ReferralRewards
            join i in _dbContext.MemberInvitations on r.InvitationId equals i.Id
            where r.TenantId == tenantId
                  && i.InvitingMemberId == invite.InvitingMemberId
                  && r.BeneficiaryRole == ReferralRewardRoles.Referrer
                  && (r.Status == ReferralRewardStatuses.PendingHold
                      || r.Status == ReferralRewardStatuses.Granted)
                  && r.CreatedAtUtc >= monthStartUtc
            select r.InvitationId
        ).Distinct().CountAsync(ct);

        if (rewardedThisMonth >= monthlyCap)
        {
            _logger.LogInformation(
                "Referral rewards skipped (monthly cap {Cap}): invitation {Id}",
                monthlyCap, invitationId);
            return;
        }

        var plan = await ResolvePlanForSaleAsync(tenantId, saleId, invite.ConvertedMemberId.Value, ct);
        var isFamily = string.Equals(plan?.PlanType, "family", StringComparison.OrdinalIgnoreCase);
        var (rewardType, rewardValue) = ResolveRewardSpec(plan, settings, isFamily);

        if (rewardValue <= 0)
        {
            _logger.LogInformation("Referral rewards skipped (zero value): invitation {Id}", invitationId);
            return;
        }

        var holdUntil = DateTime.UtcNow.AddDays(holdDays);
        var now = DateTime.UtcNow;

        _dbContext.ReferralRewards.Add(new ReferralReward
        {
            TenantId = tenantId,
            InvitationId = invitationId,
            SaleId = saleId,
            BeneficiaryMemberId = invite.InvitingMemberId,
            BeneficiaryRole = ReferralRewardRoles.Referrer,
            RewardType = rewardType,
            RewardValue = rewardValue,
            Status = ReferralRewardStatuses.PendingHold,
            HoldUntilUtc = holdUntil,
            IsFamily = isFamily,
            CreatedAtUtc = now
        });

        _dbContext.ReferralRewards.Add(new ReferralReward
        {
            TenantId = tenantId,
            InvitationId = invitationId,
            SaleId = saleId,
            BeneficiaryMemberId = invite.ConvertedMemberId.Value,
            BeneficiaryRole = ReferralRewardRoles.Referee,
            RewardType = rewardType,
            RewardValue = rewardValue,
            Status = ReferralRewardStatuses.PendingHold,
            HoldUntilUtc = holdUntil,
            IsFamily = isFamily,
            CreatedAtUtc = now
        });

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Referral pending_hold created (dual): invitation {Id}, type {Type} value {Value}, family={Family}, hold until {Hold}",
            invitationId, rewardType, rewardValue, isFamily, holdUntil);
    }

    /// <inheritdoc/>
    public async Task<int> ProcessDueHoldsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var due = await _dbContext.ReferralRewards
            .IgnoreQueryFilters()
            .Include(r => r.BeneficiaryMember)
            .Where(r => !r.IsDeleted
                     && r.Status == ReferralRewardStatuses.PendingHold
                     && r.HoldUntilUtc <= now)
            .OrderBy(r => r.HoldUntilUtc)
            .Take(100)
            .ToListAsync(ct);

        var granted = 0;
        foreach (var reward in due)
        {
            try
            {
                // Re-establish ambient tenant for audit / filters
                var tenant = await _dbContext.Tenants.IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == reward.TenantId, ct);
                if (tenant == null) continue;

                // Clear and set tenant on context via ITenantContext if available — job uses IgnoreQueryFilters writes
                if (await GrantOneAsync(reward, ct))
                    granted++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed granting referral reward {Id}", reward.Id);
            }
        }

        return granted;
    }

    /// <inheritdoc/>
    public async Task HandleConvertingSaleRefundedAsync(
        Guid tenantId, Guid saleId, CancellationToken ct = default)
    {
        var rewards = await _dbContext.ReferralRewards
            .Where(r => r.TenantId == tenantId && r.SaleId == saleId)
            .ToListAsync(ct);

        if (rewards.Count == 0)
        {
            // Also match via invitation.ConvertingSaleId
            var inviteIds = await _dbContext.MemberInvitations
                .Where(i => i.TenantId == tenantId && i.ConvertingSaleId == saleId)
                .Select(i => i.Id)
                .ToListAsync(ct);
            rewards = await _dbContext.ReferralRewards
                .Where(r => r.TenantId == tenantId && inviteIds.Contains(r.InvitationId))
                .ToListAsync(ct);
        }

        var now = DateTime.UtcNow;
        foreach (var reward in rewards)
        {
            if (reward.Status == ReferralRewardStatuses.PendingHold)
            {
                reward.Status = ReferralRewardStatuses.Forfeited;
                reward.ForfeitedAtUtc = now;
                reward.UpdatedAtUtc = now;
                await _auditService.LogAsync("referral_reward.forfeited", "ReferralReward", reward.Id,
                    null, new { saleId, reason = "converting_sale_refunded" });
            }
            else if (reward.Status == ReferralRewardStatuses.Granted)
            {
                await ReverseGrantedAsync(reward, now, ct);
            }
        }

        if (rewards.Count > 0)
            await _dbContext.SaveChangesAsync(ct);
    }

    private async Task<bool> GrantOneAsync(ReferralReward reward, CancellationToken ct)
    {
        // Idempotent re-check
        if (reward.Status != ReferralRewardStatuses.PendingHold)
            return false;

        // If converting sale was fully refunded, forfeit instead
        if (reward.SaleId.HasValue)
        {
            var saleStatus = await _dbContext.Sales.IgnoreQueryFilters()
                .Where(s => s.Id == reward.SaleId.Value)
                .Select(s => s.Status)
                .FirstOrDefaultAsync(ct);
            if (string.Equals(saleStatus, "refunded", StringComparison.OrdinalIgnoreCase))
            {
                reward.Status = ReferralRewardStatuses.Forfeited;
                reward.ForfeitedAtUtc = DateTime.UtcNow;
                reward.UpdatedAtUtc = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(ct);
                return false;
            }
        }

        var actorId = await ResolveActorAppUserIdAsync(reward.TenantId, reward.BeneficiaryMemberId, ct);
        var now = DateTime.UtcNow;

        if (string.Equals(reward.RewardType, ReferralRewardTypes.Credit, StringComparison.OrdinalIgnoreCase))
        {
            var credit = new MemberCredit
            {
                TenantId = reward.TenantId,
                MemberId = reward.BeneficiaryMemberId,
                Amount = reward.RewardValue,
                EntryType = MemberCreditEntryTypes.ReferralReward,
                ReferenceId = reward.Id,
                Reason = $"Referral reward ({reward.BeneficiaryRole})",
                CreatedByUserId = actorId
            };
            _dbContext.MemberCredits.Add(credit);
            await _dbContext.SaveChangesAsync(ct);
            reward.CreditEntryId = credit.Id;
        }
        else if (string.Equals(reward.RewardType, ReferralRewardTypes.FreeDays, StringComparison.OrdinalIgnoreCase))
        {
            var days = (int)Math.Round(reward.RewardValue, MidpointRounding.AwayFromZero);
            if (days <= 0) days = 1;

            var membership = await _dbContext.Memberships.IgnoreQueryFilters()
                .Include(m => m.Plan)
                .Where(m => m.TenantId == reward.TenantId
                         && m.MemberId == reward.BeneficiaryMemberId
                         && !m.IsDeleted
                         && (m.Status == "active" || m.Status == "frozen"))
                .OrderByDescending(m => m.EndDate)
                .FirstOrDefaultAsync(ct);

            if (membership == null)
            {
                _logger.LogWarning(
                    "Free-days grant deferred — no active membership for member {MemberId} reward {Id}",
                    reward.BeneficiaryMemberId, reward.Id);
                return false;
            }

            membership.EndDate = membership.EndDate.AddDays(days);
            membership.UpdatedAtUtc = now;
            reward.ExtendedMembershipId = membership.Id;
            reward.DaysGranted = days;

            await _auditService.LogAsync("referral_reward.free_days", "Membership", membership.Id,
                null, new { days, reward.Id, newEnd = membership.EndDate });
        }
        else
        {
            return false;
        }

        reward.Status = ReferralRewardStatuses.Granted;
        reward.GrantedAtUtc = now;
        reward.UpdatedAtUtc = now;

        // Tier escalate on referrer successful count (already incremented at convert)
        if (reward.BeneficiaryRole == ReferralRewardRoles.Referrer)
        {
            var referrer = await _dbContext.GymMembers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == reward.BeneficiaryMemberId, ct);
            if (referrer != null)
            {
                referrer.ReferralTier = ReferralTiers.FromSuccessfulCount(referrer.SuccessfulReferralCount);
                referrer.UpdatedAtUtc = now;
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        await _auditService.LogAsync("referral_reward.granted", "ReferralReward", reward.Id,
            null, new { reward.RewardType, reward.RewardValue, reward.BeneficiaryRole });

        var phone = reward.BeneficiaryMember?.PhoneNumber
            ?? await _dbContext.GymMembers.IgnoreQueryFilters()
                .Where(m => m.Id == reward.BeneficiaryMemberId)
                .Select(m => m.PhoneNumber)
                .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrEmpty(phone))
        {
            try
            {
                await _whatsApp.SendTemplateAsync(phone, "referral_reward_granted", new Dictionary<string, string>
                {
                    ["type"] = reward.RewardType,
                    ["value"] = reward.RewardValue.ToString("0.##"),
                    ["role"] = reward.BeneficiaryRole
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WhatsApp referral grant notify failed for {Phone}", phone);
            }
        }

        return true;
    }

    private async Task ReverseGrantedAsync(ReferralReward reward, DateTime now, CancellationToken ct)
    {
        var actorId = await ResolveActorAppUserIdAsync(reward.TenantId, reward.BeneficiaryMemberId, ct);

        if (string.Equals(reward.RewardType, ReferralRewardTypes.Credit, StringComparison.OrdinalIgnoreCase)
            && reward.RewardValue > 0)
        {
            _dbContext.MemberCredits.Add(new MemberCredit
            {
                TenantId = reward.TenantId,
                MemberId = reward.BeneficiaryMemberId,
                Amount = -reward.RewardValue,
                EntryType = MemberCreditEntryTypes.ReferralReward,
                ReferenceId = reward.Id,
                Reason = "Referral reward reversed (sale refunded)",
                CreatedByUserId = actorId
            });
        }
        else if (string.Equals(reward.RewardType, ReferralRewardTypes.FreeDays, StringComparison.OrdinalIgnoreCase)
                 && reward.DaysGranted is > 0
                 && reward.ExtendedMembershipId.HasValue)
        {
            var membership = await _dbContext.Memberships
                .FirstOrDefaultAsync(m => m.Id == reward.ExtendedMembershipId.Value
                                       && m.TenantId == reward.TenantId, ct);
            if (membership != null)
            {
                membership.EndDate = membership.EndDate.AddDays(-reward.DaysGranted.Value);
                membership.UpdatedAtUtc = now;
                await _auditService.LogAsync("referral_reward.free_days_reversed", "Membership",
                    membership.Id, null, new { days = reward.DaysGranted, reward.Id });
            }
        }

        reward.Status = ReferralRewardStatuses.Reversed;
        reward.ReversedAtUtc = now;
        reward.UpdatedAtUtc = now;

        await _auditService.LogAsync("referral_reward.reversed", "ReferralReward", reward.Id,
            null, new { reason = "converting_sale_refunded" });
    }

    private async Task<MembershipPlan?> ResolvePlanForSaleAsync(
        Guid tenantId, Guid? saleId, Guid convertedMemberId, CancellationToken ct)
    {
        if (saleId.HasValue)
        {
            var planId = await _dbContext.SaleLines
                .Where(l => l.SaleId == saleId && l.TenantId == tenantId && l.LineType == "membership")
                .Join(_dbContext.Memberships,
                    l => l.ReferenceId, m => m.Id, (l, m) => m.PlanId)
                .FirstOrDefaultAsync(ct);

            if (planId != Guid.Empty)
            {
                return await _dbContext.MembershipPlans
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == planId && p.TenantId == tenantId, ct);
            }
        }

        var today = MembershipOperational.TodayCairo();
        var memberships = await _dbContext.Memberships
            .Include(m => m.Plan)
            .Where(m => m.MemberId == convertedMemberId && m.TenantId == tenantId)
            .ToListAsync(ct);

        return memberships
            .OrderByDescending(m => m.PaymentDate ?? m.CreatedAtUtc)
            .Select(m => m.Plan)
            .FirstOrDefault();
    }

    private static (string Type, decimal Value) ResolveRewardSpec(
        MembershipPlan? plan, string? settings, bool isFamily)
    {
        string type;
        decimal value;

        if (plan != null
            && !string.IsNullOrWhiteSpace(plan.ReferralRewardType)
            && plan.ReferralRewardValue is > 0
            && ReferralRewardTypes.All.Contains(plan.ReferralRewardType))
        {
            type = plan.ReferralRewardType.Trim().ToLowerInvariant();
            value = plan.ReferralRewardValue.Value;
        }
        else
        {
            var threshold = GetSettingDecimal(settings, TenantSettingsKeys.ReferralRewardPriceThresholdEgp, 1500m);
            var price = plan?.Price ?? 0m;
            if (price >= threshold)
            {
                type = ReferralRewardTypes.FreeDays;
                value = GetSettingInt(settings, TenantSettingsKeys.ReferralDefaultFreeDays, 7);
            }
            else
            {
                type = ReferralRewardTypes.Credit;
                value = GetSettingDecimal(settings, TenantSettingsKeys.ReferralDefaultCreditEgp, 50m);
            }
        }

        if (isFamily)
        {
            var multiplier = GetSettingDecimal(
                settings, TenantSettingsKeys.ReferralFamilyRewardMultiplier, 1.5m);
            if (multiplier > 1m)
            {
                value = Math.Round(value * multiplier, 2, MidpointRounding.AwayFromZero);
                if (string.Equals(type, ReferralRewardTypes.FreeDays, StringComparison.OrdinalIgnoreCase))
                    value = Math.Max(1, Math.Round(value, MidpointRounding.AwayFromZero));
            }
        }

        return (type, value);
    }

    private async Task<Guid> ResolveActorAppUserIdAsync(
        Guid tenantId, Guid beneficiaryMemberId, CancellationToken ct)
    {
        var appUserId = await _dbContext.GymMembers.IgnoreQueryFilters()
            .Where(m => m.Id == beneficiaryMemberId)
            .Select(m => m.AppUserId)
            .FirstOrDefaultAsync(ct);
        if (appUserId.HasValue && appUserId != Guid.Empty)
            return appUserId.Value;

        var owner = await _dbContext.AppUsers.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && !u.IsDeleted
                     && (u.Role == "Owner" || u.Role == "owner" || u.Role == "admin"))
            .OrderBy(u => u.CreatedAtUtc)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);

        if (owner != Guid.Empty)
            return owner;

        // Last resort: any app user in tenant (FK required)
        return await _dbContext.AppUsers.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && !u.IsDeleted)
            .Select(u => u.Id)
            .FirstAsync(ct);
    }

    private static decimal GetSettingDecimal(string? settingsJson, string key, decimal defaultValue)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return defaultValue;
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (doc.RootElement.TryGetProperty(key, out var prop) && prop.TryGetDecimal(out var value))
                return value;
        }
        catch { /* default */ }
        return defaultValue;
    }

    private static int GetSettingInt(string? settingsJson, string key, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return defaultValue;
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (doc.RootElement.TryGetProperty(key, out var prop) && prop.TryGetInt32(out var value))
                return value;
        }
        catch { /* default */ }
        return defaultValue;
    }
}
