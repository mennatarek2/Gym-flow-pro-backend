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
        Guid memberId, Guid tenantId, string? rawPayload, bool hmacVerified,
        Guid? saleId = null, string? paymentMethod = null)
    {
        if (tenantId == Guid.Empty || (!saleId.HasValue && memberId == Guid.Empty))
            return Result<string>.Failure("Payment identity is incomplete.");
        if (string.IsNullOrWhiteSpace(gateway) || string.IsNullOrWhiteSpace(externalRef))
            return Result<string>.Failure("Payment gateway reference is required.");
        if (amount <= 0m)
            return Result<string>.Failure("Payment amount must be greater than zero.");
        if (!hmacVerified)
            return Result<string>.Failure("Payment signature was not verified.");

        gateway = gateway.Trim().ToLowerInvariant();
        externalRef = externalRef.Trim();
        paymentMethod = string.IsNullOrWhiteSpace(paymentMethod)
            ? gateway
            : paymentMethod.Trim().ToLowerInvariant();

        // Webhooks are AllowAnonymous — TenantMiddleware does not SetTenant. Establish ambient
        // context from the verified payload tenant so EF filters and membership queries work.
        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted);

        if (tenant == null)
            return Result<string>.Failure("Tenant not found");

        _tenantContext.SetTenant(tenant.Id, tenant.Name, tenant.TimeZone);

        var isRelational = _dbContext.Database.IsRelational();
        await using var transaction = isRelational
            ? await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;

        // Gateway references are idempotent only inside their tenant and gateway.
        var existingTx = await _dbContext.PaymentTransactions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId
                && p.Gateway == gateway
                && p.ExternalRef == externalRef);

        PaymentTransaction? salePayment = null;
        if (existingTx != null)
        {
            if (existingTx.Amount != amount || existingTx.SaleId != saleId)
                return Result<string>.Failure("Payment reference was already used for different payment data.");
            if (existingTx.Status == "success")
            {
                _logger.LogInformation(
                    "[Payment] Duplicate webhook ignored: {Gateway} ExternalRef={Ref}",
                    gateway, externalRef);
                return Result<string>.Success("Duplicate — already processed");
            }
            if (existingTx.Status != "failed")
                return Result<string>.Failure("Payment reference is not retryable.");
            salePayment = existingTx;
        }

        if (!saleId.HasValue)
            return Result<string>.Failure("Payment source sale is required.");

        var sale = await _dbContext.Sales
            .FirstOrDefaultAsync(s => s.Id == saleId.Value && s.TenantId == tenantId, CancellationToken.None);
        if (sale == null)
            return Result<string>.Failure("Payment source sale was not found.");
        if (sale.Status is "refunded" or "cancelled")
            return Result<string>.Failure("Payment source sale is not collectable.");
        if (sale.MemberId.HasValue && memberId != Guid.Empty && sale.MemberId != memberId)
            return Result<string>.Failure("Payment member does not match the source sale.");
        if (!sale.MemberId.HasValue && memberId != Guid.Empty)
            return Result<string>.Failure("A memberless sale cannot use a member identity.");
        var allocated = await _dbContext.PaymentTransactions
            .Where(payment => payment.TenantId == tenantId
                && payment.SaleId == sale.Id
                && payment.Status == "success"
                && payment.Amount > 0m
                && (salePayment == null || payment.Id != salePayment.Id))
            .SumAsync(payment => (decimal?)payment.Amount) ?? 0m;
        var adjustments = await _dbContext.SaleAdjustments
            .Where(adjustment => adjustment.TenantId == tenantId
                && adjustment.SaleId == sale.Id
                && adjustment.Status == "posted")
            .SumAsync(adjustment => (decimal?)adjustment.Amount) ?? 0m;
        var canonicalDue = Math.Max(0m, decimal.Round(
            sale.Total - allocated - adjustments, 2, MidpointRounding.AwayFromZero));
        if (Math.Abs(sale.AmountDue - canonicalDue) > 0.01m)
            return Result<string>.Failure("SALE_RECONCILIATION_REQUIRED");
        if (amount > canonicalDue)
            return Result<string>.Failure("Payment amount exceeds the outstanding sale balance.");

        sale.AmountDue = decimal.Round(sale.AmountDue - amount, 2, MidpointRounding.AwayFromZero);
        sale.Status = sale.AmountDue == 0m ? "completed" : "partially_paid";
        sale.UpdatedAtUtc = DateTime.UtcNow;

        // Membership sales are commercial Sale facts first. The membership remains pending
        // until the sale is fully paid; the verified payment event is the only activation path
        // for gateway-created memberships.
        var membershipId = await _dbContext.SaleLines
            .Where(line => line.SaleId == sale.Id
                && line.TenantId == tenantId
                && line.LineType == "membership")
            .Select(line => line.ReferenceId)
            .FirstOrDefaultAsync();
        if (membershipId.HasValue)
        {
            var membership = await _dbContext.Memberships
                .FirstOrDefaultAsync(item => item.Id == membershipId.Value && item.TenantId == tenantId);
            if (membership != null)
            {
                membership.AmountPaid = decimal.Round(membership.AmountPaid + amount, 2, MidpointRounding.AwayFromZero);
                membership.PaymentDate = DateTime.UtcNow;
                if (sale.AmountDue == 0m)
                    membership.Status = "active";
                membership.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        salePayment ??= new PaymentTransaction
        {
            TenantId = tenantId,
            MemberId = sale.MemberId ?? (memberId == Guid.Empty ? null : memberId),
            Gateway = gateway,
            ExternalRef = externalRef,
            Amount = amount,
            Currency = "EGP",
            Status = "success",
            // A verified provider success callback is external settlement evidence.
            // Historical rows are never upgraded by this path.
            SettlementStatus = "settled",
            SettledAtUtc = DateTime.UtcNow,
            RawPayload = rawPayload,
            HmacVerified = true,
            PaidAtUtc = DateTime.UtcNow,
            SaleId = sale.Id,
            Method = paymentMethod
        };
        if (salePayment.Id == Guid.Empty)
            _dbContext.PaymentTransactions.Add(salePayment);
        else
        {
            salePayment.Status = "success";
            salePayment.SettlementStatus = "pending";
            salePayment.RawPayload = rawPayload;
            salePayment.HmacVerified = true;
            salePayment.PaidAtUtc = DateTime.UtcNow;
            salePayment.SaleId = sale.Id;
            salePayment.Method = paymentMethod;
        }
        await _dbContext.SaveChangesAsync();
        if (transaction != null)
            await transaction.CommitAsync();

        _logger.LogInformation(
            "[Payment] Processed settled-source event: {Gateway} {Ref} → Sale {SaleId}, amount {Amount} EGP",
            gateway, externalRef, sale.Id, amount);
        return Result<string>.Success($"Payment recorded for sale {sale.Id}");
    }

    public async Task<Result<string>> ConfirmSettlementAsync(
        Guid paymentTransactionId,
        Guid tenantId,
        string gateway,
        string externalRef,
        string? rawPayload,
        bool externalEvidenceVerified)
    {
        if (!externalEvidenceVerified)
            return Result<string>.Failure("Settlement evidence was not verified.");
        if (tenantId == Guid.Empty || paymentTransactionId == Guid.Empty
            || string.IsNullOrWhiteSpace(gateway) || string.IsNullOrWhiteSpace(externalRef))
            return Result<string>.Failure("Settlement identity is incomplete.");

        var payment = await _dbContext.PaymentTransactions
            .FirstOrDefaultAsync(item => item.Id == paymentTransactionId
                && item.TenantId == tenantId
                && item.Gateway == gateway.Trim().ToLowerInvariant()
                && item.ExternalRef == externalRef.Trim());
        if (payment == null)
            return Result<string>.Failure("Payment transaction was not found.");
        if (payment.Status == "failed" || payment.Status == "reversed")
            return Result<string>.Failure("Only a successful payment can be settled.");
        if (payment.SettlementStatus == "settled")
            return Result<string>.Success("Settlement already recorded.");

        payment.SettlementStatus = "settled";
        payment.SettledAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(rawPayload))
            payment.RawPayload = rawPayload;
        await _dbContext.SaveChangesAsync();
        return Result<string>.Success("Payment settlement recorded.");
    }

    public async Task<Result<string>> RecordFailedPaymentAsync(
        string gateway, string externalRef, decimal amount,
        Guid memberId, Guid tenantId, string? rawPayload, bool hmacVerified,
        Guid? saleId = null, string? paymentMethod = null)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(gateway)
            || string.IsNullOrWhiteSpace(externalRef) || amount <= 0m || !hmacVerified)
            return Result<string>.Failure("Failed payment event is incomplete or unverified.");

        gateway = gateway.Trim().ToLowerInvariant();
        externalRef = externalRef.Trim();
        paymentMethod = string.IsNullOrWhiteSpace(paymentMethod)
            ? gateway
            : paymentMethod.Trim().ToLowerInvariant();

        var tenant = await _dbContext.Tenants.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == tenantId && !item.IsDeleted);
        if (tenant == null)
            return Result<string>.Failure("Tenant not found");
        _tenantContext.SetTenant(tenant.Id, tenant.Name, tenant.TimeZone);

        var isRelational = _dbContext.Database.IsRelational();
        await using var transaction = isRelational
            ? await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable)
            : null;
        var existing = await _dbContext.PaymentTransactions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.TenantId == tenantId
                && item.Gateway == gateway
                && item.ExternalRef == externalRef);
        if (existing != null)
            return existing.Amount == amount && existing.Status == "failed"
                ? Result<string>.Success("Duplicate — already processed")
                : Result<string>.Failure("Payment reference was already used for different payment data.");

        if (saleId.HasValue)
        {
            var sourceExists = await _dbContext.Sales.AnyAsync(item =>
                item.Id == saleId.Value && item.TenantId == tenantId);
            if (!sourceExists)
                return Result<string>.Failure("Payment source sale was not found.");
        }

        _dbContext.PaymentTransactions.Add(new PaymentTransaction
        {
            TenantId = tenantId,
            MemberId = memberId == Guid.Empty ? null : memberId,
            Gateway = gateway,
            ExternalRef = externalRef,
            Amount = amount,
            Currency = "EGP",
            Status = "failed",
            SettlementStatus = "failed",
            RawPayload = rawPayload,
            HmacVerified = true,
            PaidAtUtc = DateTime.UtcNow,
            SaleId = saleId,
            Method = paymentMethod
        });
        await _dbContext.SaveChangesAsync();
        if (transaction != null)
            await transaction.CommitAsync();
        return Result<string>.Success("Failed payment recorded");
    }
}
