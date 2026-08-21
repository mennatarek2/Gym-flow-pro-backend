namespace GMS.Application.Services;

using Hangfire;
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
/// Payment processing service.
///
/// HandleSuccessfulPaymentAsync is called by payment webhook controllers.
/// Prefer activating an existing unpaid pending membership (desk renew) so stored
/// dates and PlanTransitionMode are honored; otherwise create a new active row.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly GymFlowProDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IReferralAttributionService _referralAttribution;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        GymFlowProDbContext dbContext,
        ITenantContext tenantContext,
        IReferralAttributionService referralAttribution,
        ILogger<PaymentService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _referralAttribution = referralAttribution;
        _logger = logger;
    }

    public async Task<Result<string>> HandleSuccessfulPaymentAsync(
        string gateway, string externalRef, decimal amount,
        Guid memberId, Guid tenantId, string? rawPayload, bool hmacVerified)
    {
        // Webhooks are AllowAnonymous — TenantMiddleware does not SetTenant. Establish ambient
        // context from the verified payload tenant so EF filters and membership queries work.
        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted);

        if (tenant == null)
            return Result<string>.Failure("Tenant not found");

        _tenantContext.SetTenant(tenant.Id, tenant.Name, tenant.TimeZone);

        // === STEP 1: Idempotency check (global ExternalRef) ===
        var existingTx = await _dbContext.PaymentTransactions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.ExternalRef == externalRef);

        if (existingTx != null)
        {
            _logger.LogInformation(
                "[Payment] Duplicate webhook ignored: {Gateway} ExternalRef={Ref}",
                gateway, externalRef);
            return Result<string>.Success("Duplicate — already processed");
        }

        var member = await _dbContext.GymMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.TenantId == tenantId);

        if (member == null)
            return Result<string>.Failure("Member not found");

        var today = MembershipOperational.TodayCairo();
        var allMemberships = await _dbContext.Memberships
            .Include(m => m.Plan)
            .Where(m => m.MemberId == memberId && m.TenantId == tenantId)
            .ToListAsync();

        // Prefer unpaid pending renew created at the desk (dates + transition already baked).
        var pendingMembership = allMemberships
            .Where(m => m.Status == "pending" && m.PaymentDate == null)
            .OrderByDescending(m => m.CreatedAtUtc)
            .FirstOrDefault();

        Membership activated;
        if (pendingMembership != null)
        {
            var mode = string.IsNullOrWhiteSpace(pendingMembership.PlanTransitionMode)
                ? PlanTransitionModes.CancelAndSwitch
                : pendingMembership.PlanTransitionMode!;

            pendingMembership.Status = "active";
            pendingMembership.PaymentMethod = gateway;
            pendingMembership.AmountPaid = amount;
            pendingMembership.PaymentDate = DateTime.UtcNow;
            pendingMembership.UpdatedAtUtc = DateTime.UtcNow;

            MembershipRenewalDating.ApplyPriorOpenHandling(
                allMemberships, pendingMembership.Id, mode, today, apply: true);

            activated = pendingMembership;
        }
        else
        {
            var covering = MembershipOperational.SelectCoveringToday(allMemberships, today);
            var planSource = covering
                             ?? MembershipOperational.SelectOperational(allMemberships, today)
                             ?? allMemberships.OrderByDescending(m => m.EndDate).FirstOrDefault();

            if (planSource?.Plan == null)
                return Result<string>.Failure("No membership plan found for renewal");

            // Webhook without desk pending: start a fresh period (no forced rollover).
            var mode = PlanTransitionModes.CancelAndSwitch;
            var (newStartDate, newEndDate) = MembershipRenewalDating.Calculate(
                covering ?? planSource, planSource.Plan, mode, today);

            activated = new Membership
            {
                TenantId = tenantId,
                MemberId = memberId,
                PlanId = planSource.PlanId,
                StartDate = newStartDate,
                EndDate = newEndDate,
                Status = "active",
                SessionsRemaining = planSource.Plan.PlanType == "session_pack"
                    ? planSource.Plan.SessionCount
                    : null,
                PaymentMethod = gateway,
                AmountPaid = amount,
                PaymentDate = DateTime.UtcNow,
                PlanTransitionMode = mode,
                CreatedAtUtc = DateTime.UtcNow
            };

            await _dbContext.Memberships.AddAsync(activated);
            MembershipRenewalDating.ApplyPriorOpenHandling(
                allMemberships, activated.Id, mode, today, apply: true);
        }

        var paymentTx = new PaymentTransaction
        {
            TenantId = tenantId,
            MemberId = memberId,
            MembershipId = activated.Id,
            Gateway = gateway,
            ExternalRef = externalRef,
            Amount = amount,
            Status = "success",
            RawPayload = rawPayload,
            HmacVerified = hmacVerified,
            PaidAtUtc = DateTime.UtcNow
        };

        await _dbContext.PaymentTransactions.AddAsync(paymentTx);
        await _dbContext.SaveChangesAsync();

        var planType = activated.Plan?.PlanType
            ?? (await _dbContext.MembershipPlans.AsNoTracking()
                .Where(p => p.Id == activated.PlanId)
                .Select(p => p.PlanType)
                .FirstOrDefaultAsync())
            ?? string.Empty;

        try
        {
            await _referralAttribution.TryConvertOnPaidActivateAsync(
                tenantId, memberId, saleId: paymentTx.SaleId, amount, planType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Referral convert failed after payment {Ref}", externalRef);
        }

        _logger.LogInformation(
            "[Payment] Processed: {Gateway} {Ref} → Member {MemberNumber}, " +
            "membership {Start}–{End} (mode={Mode}), amount {Amount} EGP",
            gateway, externalRef, member.MemberNumber,
            activated.StartDate, activated.EndDate,
            activated.PlanTransitionMode ?? PlanTransitionModes.CancelAndSwitch,
            amount);

        BackgroundJob.Enqueue<IWhatsAppService>(
            svc => svc.SendRenewalConfirmationAsync(
                member.PhoneNumber, member.FullName, activated.EndDate.ToDateTime(TimeOnly.MinValue)));

        return Result<string>.Success($"Membership renewed until {activated.EndDate}");
    }
}
