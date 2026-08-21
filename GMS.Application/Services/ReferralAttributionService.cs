namespace GMS.Application.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Referral attribution: pending attach at desk, convert on paid activate (no rewards).
/// </summary>
public class ReferralAttributionService : IReferralAttributionService
{
    private static readonly HashSet<string> NonConvertingPlanTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "trial",
        "day_pass"
    };

    private readonly GymFlowProDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly IEncryptionService _encryption;
    private readonly IReferralRewardService _referralRewards;
    private readonly ILogger<ReferralAttributionService> _logger;

    public ReferralAttributionService(
        GymFlowProDbContext dbContext,
        IAuditService auditService,
        IEncryptionService encryption,
        IReferralRewardService referralRewards,
        ILogger<ReferralAttributionService> logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _encryption = encryption;
        _referralRewards = referralRewards;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<Guid>> ResolveReferrerAsync(
        Guid tenantId,
        string? referralCode,
        Guid? referringMemberId,
        string guestPhoneNormalized,
        string? guestNationalIdPlain = null)
    {
        if (string.IsNullOrWhiteSpace(referralCode) && !referringMemberId.HasValue)
            return Result<Guid>.Failure("Referral code or referring member is required");

        var referrer = await LoadReferrerAsync(tenantId, referralCode, referringMemberId);
        if (referrer == null)
            return Result<Guid>.Failure("Referral code not found / كود الإحالة غير موجود");

        var selfBlock = CheckSelfReferral(referrer, guestPhoneNormalized, guestNationalIdPlain);
        if (selfBlock != null)
            return Result<Guid>.Failure(selfBlock);

        return Result<Guid>.Success(referrer.Id);
    }

    /// <inheritdoc/>
    public Task<Result> AttachPendingAsync(
        Guid tenantId,
        Guid convertedMemberId,
        string? referralCode,
        Guid? referringMemberId)
    {
        // Referral codes are retired. Attribution is the Invitation row (phone match on paid activate).
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc/>
    public async Task TryConvertOnPaidActivateAsync(
        Guid tenantId,
        Guid convertedMemberId,
        Guid? saleId,
        decimal amountPaid,
        string planType,
        CancellationToken ct = default)
    {
        if (NonConvertingPlanTypes.Contains(planType ?? string.Empty))
        {
            _logger.LogInformation(
                "Invitation convert skipped (plan {PlanType}) for member {MemberId}",
                planType, convertedMemberId);
            return;
        }

        var converted = await _dbContext.GymMembers
            .FirstOrDefaultAsync(m => m.Id == convertedMemberId && m.TenantId == tenantId, ct);
        if (converted == null)
            return;

        var invite = await _dbContext.MemberInvitations
            .Where(i => i.TenantId == tenantId
                     && i.InvitationType == InvitationTypes.Invitation
                     && i.GuestPhoneNumber == converted.PhoneNumber
                     && i.Status != InvitationStatuses.Converted)
            .OrderBy(i => i.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (invite == null)
        {
            invite = await _dbContext.MemberInvitations
                .FirstOrDefaultAsync(i => i.TenantId == tenantId
                                       && i.InvitationType == InvitationTypes.Referral
                                       && i.ConvertedMemberId == convertedMemberId
                                       && i.Status == "pending", ct);
        }

        if (invite == null)
            return;

        var isProductInvitation = string.Equals(
            invite.InvitationType, InvitationTypes.Invitation, StringComparison.OrdinalIgnoreCase);

        if (!isProductInvitation)
        {
            var tenant = await _dbContext.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
            var minAmount = GetSettingDecimal(
                tenant?.Settings, TenantSettingsKeys.ReferralMinSaleAmountEgp, 0m);
            if (amountPaid < minAmount)
            {
                _logger.LogInformation(
                    "Referral convert skipped (amount {Amount} < min {Min}): invitation {Id}",
                    amountPaid, minAmount, invite.Id);
                return;
            }
        }

        var now = DateTime.UtcNow;
        invite.Status = InvitationStatuses.Converted;
        invite.ConvertedMemberId = convertedMemberId;
        invite.ConvertedAtUtc = now;
        invite.ConvertingSaleId = saleId;
        invite.UpdatedAtUtc = now;

        if (!isProductInvitation)
        {
            var referrer = await _dbContext.GymMembers
                .FirstOrDefaultAsync(m => m.Id == invite.InvitingMemberId && m.TenantId == tenantId, ct);
            if (referrer != null)
            {
                referrer.SuccessfulReferralCount += 1;
                referrer.UpdatedAtUtc = now;
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        await _auditService.LogAsync(
            "invitation.convert",
            "MemberInvitation",
            invite.Id,
            null,
            new
            {
                invite.InvitingMemberId,
                ConvertedMemberId = convertedMemberId,
                InvitationId = invite.Id,
                SaleId = saleId,
                AmountPaid = amountPaid,
                PlanType = planType
            });

        if (!isProductInvitation)
        {
            try
            {
                await _referralRewards.CreateHoldsForConvertedInvitationAsync(
                    tenantId, invite.Id, saleId, amountPaid, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to queue referral reward holds for invitation {Id}", invite.Id);
            }
        }

        _logger.LogInformation(
            "Invitation converted: {Id} inviting member {Inviter} sale {SaleId}",
            invite.Id, invite.InvitingMemberId, saleId);
    }

    private async Task<GymMember?> LoadReferrerAsync(
        Guid tenantId, string? referralCode, Guid? referringMemberId)
    {
        if (referringMemberId.HasValue)
        {
            return await _dbContext.GymMembers
                .FirstOrDefaultAsync(m => m.Id == referringMemberId.Value
                                       && m.TenantId == tenantId
                                       && m.IsActive);
        }

        var code = referralCode!.Trim().ToUpperInvariant();
        return await _dbContext.GymMembers
            .FirstOrDefaultAsync(m => m.TenantId == tenantId
                                   && m.ReferralCode == code
                                   && m.IsActive);
    }

    private string? CheckSelfReferral(
        GymMember referrer, string guestPhoneNormalized, string? guestNationalIdPlain)
    {
        if (referrer.PhoneNumber == guestPhoneNormalized)
            return "Self-referral is not allowed / لا يمكن إحالة نفسك";

        if (!string.IsNullOrWhiteSpace(guestNationalIdPlain)
            && !string.IsNullOrWhiteSpace(referrer.NationalIdEncrypted))
        {
            var refNid = TryDecryptNationalId(referrer.NationalIdEncrypted);
            if (!string.IsNullOrEmpty(refNid)
                && string.Equals(refNid, guestNationalIdPlain.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return "Self-referral is not allowed / لا يمكن إحالة نفسك";
            }
        }

        return null;
    }

    private string? TryDecryptNationalId(string? encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted))
            return null;
        try
        {
            return _encryption.Decrypt(encrypted);
        }
        catch
        {
            return null;
        }
    }

    private static decimal GetSettingDecimal(string? settingsJson, string key, decimal defaultValue)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
            return defaultValue;
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (doc.RootElement.TryGetProperty(key, out var prop)
                && prop.TryGetDecimal(out var value))
                return value;
        }
        catch
        {
            // fall through
        }
        return defaultValue;
    }
}
