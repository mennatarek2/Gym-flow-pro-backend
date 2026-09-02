namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Memberships;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Membership lifecycle: assign, renew, history.
/// Cash assign/renew creates a Sale + PaymentTransaction and records shift cash movement
/// (same drawer invariant as SaleService). Gateway-pending memberships remain pending until
/// their payment flow is migrated to a source Sale.
/// </summary>
public class MembershipService : IMembershipService
{
    private readonly GymFlowProDbContext _dbContext;
    private readonly IRepository<Membership> _membershipRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IShiftService _shiftService;
    private readonly IInvoiceService _invoiceService;
    private readonly IAuditService _auditService;
    private readonly IReferralAttributionService _referralAttribution;
    private readonly IActivityEntitlementService _activityEntitlements;
    private readonly ILogger<MembershipService> _logger;

    public MembershipService(
        GymFlowProDbContext dbContext,
        IRepository<Membership> membershipRepository,
        ITenantContext tenantContext,
        IShiftService shiftService,
        IInvoiceService invoiceService,
        IAuditService auditService,
        IReferralAttributionService referralAttribution,
        IActivityEntitlementService activityEntitlements,
        ILogger<MembershipService> logger)
    {
        _dbContext = dbContext;
        _membershipRepository = membershipRepository;
        _tenantContext = tenantContext;
        _shiftService = shiftService;
        _invoiceService = invoiceService;
        _auditService = auditService;
        _referralAttribution = referralAttribution;
        _activityEntitlements = activityEntitlements;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<MembershipDto>> GetCurrentMembershipAsync(Guid memberId)
    {
        try
        {
            var memberships = await _dbContext.Memberships
                .Include(m => m.Plan)
                .Where(m => m.MemberId == memberId)
                .ToListAsync();

            var selected = MembershipOperational.SelectOperational(memberships);
            if (selected == null)
            {
                return Result<MembershipDto>.Failure(
                    "No membership found for this member / لا توجد عضوية لهذا العضو");
            }

            return Result<MembershipDto>.Success(await MapToDtoAsync(selected));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current membership for member {MemberId}", memberId);
            return Result<MembershipDto>.Failure(
                "Failed to retrieve membership / فشل في جلب العضوية",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<PagedResult<MembershipHistoryItemDto>>> GetMembershipHistoryAsync(
        Guid memberId, int page, int pageSize)
    {
        try
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _dbContext.Memberships
                .Include(m => m.Plan)
                .Where(m => m.MemberId == memberId)
                .OrderByDescending(m => m.EndDate);

            var totalCount = await query.CountAsync();

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var dtoItems = items.Select(m => new MembershipHistoryItemDto
            {
                Id = m.Id,
                PlanName = m.Plan!.Name,
                PlanNameAr = m.Plan!.NameAr,
                PlanType = m.Plan!.PlanType,
                StartDate = m.StartDate,
                EndDate = m.EndDate,
                Status = MembershipOperational.GetEffectiveStatus(m),
                AmountPaid = m.AmountPaid,
                PaymentMethod = m.PaymentMethod,
                PaymentDate = m.PaymentDate,
                CreatedAtUtc = m.CreatedAtUtc
            }).ToList();

            return Result<PagedResult<MembershipHistoryItemDto>>.Success(new PagedResult<MembershipHistoryItemDto>
            {
                Items = dtoItems,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving membership history for member {MemberId}", memberId);
            return Result<PagedResult<MembershipHistoryItemDto>>.Failure(
                "Failed to retrieve membership history / فشل في جلب سجل العضويات",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<MembershipDto>> AssignMembershipAsync(
        Guid tenantId, Guid memberId, AssignMembershipRequest request, Guid staffUserId)
    {
        try
        {
            var member = await _dbContext.GymMembers
                .FirstOrDefaultAsync(m => m.Id == memberId && m.TenantId == tenantId);

            if (member == null)
                return Result<MembershipDto>.Failure(
                    "Member not found / العضو غير موجود");

            var memberships = await _dbContext.Memberships
                .Where(m => m.MemberId == memberId && m.TenantId == tenantId)
                .ToListAsync();

            var today = MembershipOperational.TodayCairo();
            var marked = false;
            foreach (var m in memberships)
            {
                if (MembershipOperational.TryMarkExpired(m, today))
                    marked = true;
            }
            if (marked)
                await _dbContext.SaveChangesAsync();

            // Block assign when operational membership is still usable or scheduled/frozen.
            var operational = MembershipOperational.SelectOperational(memberships, today);
            var effective = operational != null
                ? MembershipOperational.GetEffectiveStatus(operational, today)
                : "none";

            if (effective is "active" or "scheduled" or "frozen")
            {
                return Result<MembershipDto>.Failure(
                    "Member already has an active membership / العضو لديه عضوية نشطة بالفعل",
                    $"Member cannot have multiple active memberships. Current membership ({effective}) ends on {operational!.EndDate:yyyy-MM-dd}.");
            }

            var plan = await _dbContext.MembershipPlans
                .FirstOrDefaultAsync(p => p.Id == request.PlanId && p.TenantId == tenantId && p.IsActive);

            if (plan == null)
                return Result<MembershipDto>.Failure(
                    "Membership plan not found or inactive / الخطة غير موجودة أو غير نشطة");

            var endDate = plan.PlanType == "day_pass" ? today : today.AddDays(plan.DurationDays);
            var isCash = IsCashPayment(request.PaymentMethod);
            if (isCash && request.AmountPaid is < 0)
                return Result<MembershipDto>.Failure(
                    "Amount paid cannot be negative / المبلغ المدفوع لا يمكن أن يكون سالباً");
            var cashTaken = isCash ? (request.AmountPaid ?? plan.Price) : 0m;

            Guid? shiftId = null;
            AppUser? staffUser = null;
            if (isCash)
            {
                var cashSetup = await ResolveCashStaffAndShiftAsync(staffUserId, tenantId, cashTaken > 0);
                if (!cashSetup.IsSuccess)
                    return Result<MembershipDto>.Failure(cashSetup.Error!, cashSetup.Message);
                staffUser = cashSetup.Data!.StaffUser;
                shiftId = cashSetup.Data.ShiftId;
            }

            // Paying/assigning a plan restores the account — gym ops never leave a paid member Inactive.
            if (!member.IsActive)
            {
                member.IsActive = true;
                member.UpdatedAtUtc = DateTime.UtcNow;
            }

            var newMembership = new Membership
            {
                TenantId = tenantId,
                MemberId = memberId,
                PlanId = request.PlanId,
                StartDate = today,
                EndDate = endDate,
                Status = isCash ? "active" : "pending",
                SessionsRemaining = plan.PlanType == "session_pack" ? plan.SessionCount : null,
                PaymentMethod = request.PaymentMethod,
                AmountPaid = isCash ? cashTaken : 0m,
                PaymentDate = isCash ? DateTime.UtcNow : null,
                CreatedAtUtc = DateTime.UtcNow
            };

            if (isCash)
                await SyncMemberInvitationQuotaAsync(member, plan);

            var attachReferral = await _referralAttribution.AttachPendingAsync(
                tenantId, memberId, request.ReferralCode, request.ReferringMemberId);
            if (!attachReferral.IsSuccess)
                return Result<MembershipDto>.Failure(attachReferral.Error!);

            if (isCash && staffUser != null)
            {
                var saleId = await PersistPaidMembershipAsync(
                    member, newMembership, plan, staffUser, shiftId, staffUserId, tenantId,
                    amountPaid: cashTaken, paymentMethod: request.PaymentMethod);

                await _referralAttribution.TryConvertOnPaidActivateAsync(
                    tenantId, memberId, saleId, cashTaken, plan.PlanType);

                _logger.LogInformation(
                    "Membership assigned (cash+sale): MemberId={MemberId}, PlanId={PlanId}, EndDate={EndDate}, ShiftId={ShiftId}",
                    memberId, request.PlanId, endDate, shiftId);
            }
            else
            {
                await _membershipRepository.AddAsync(newMembership);
                _logger.LogInformation(
                    "Membership created (pending payment): MemberId={MemberId}, PlanId={PlanId}, PaymentMethod={PaymentMethod}",
                    memberId, request.PlanId, request.PaymentMethod);
            }

            return await GetCurrentMembershipAsync(memberId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning membership for member {MemberId}", memberId);
            return Result<MembershipDto>.Failure(
                "Failed to assign membership / فشل في تعيين العضوية",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<MembershipDto>> RenewMembershipAsync(
        Guid tenantId, Guid memberId, RenewMembershipRequest request, Guid staffUserId)
    {
        try
        {
            if (!PlanTransitionModes.TryNormalize(request.TransitionMode, out var transitionMode))
                return Result<MembershipDto>.Failure(
                    "Invalid transitionMode. Use cancel_and_switch, queue_next, or manual_rollover / وضع الانتقال غير صالح");

            var member = await _dbContext.GymMembers
                .FirstOrDefaultAsync(m => m.Id == memberId && m.TenantId == tenantId);

            if (member == null)
                return Result<MembershipDto>.Failure(
                    "Member not found / العضو غير موجود");

            // Renewing restores account access (same as assign).
            if (!member.IsActive)
            {
                member.IsActive = true;
                member.UpdatedAtUtc = DateTime.UtcNow;
            }

            var allMemberships = await _dbContext.Memberships
                .Include(m => m.Plan)
                .Where(m => m.MemberId == memberId && m.TenantId == tenantId)
                .ToListAsync();

            var today = MembershipOperational.TodayCairo();
            var coveringMembership = MembershipOperational.SelectCoveringToday(allMemberships, today);
            var operationalPrior = MembershipOperational.SelectOperational(allMemberships, today);
            var planSource = coveringMembership ?? operationalPrior
                             ?? allMemberships.OrderByDescending(m => m.EndDate).FirstOrDefault();

            if (planSource?.Plan == null && !request.PlanId.HasValue)
                return Result<MembershipDto>.Failure(
                    "No membership to renew / لا توجد عضوية للتجديد");

            Guid planId = request.PlanId ?? planSource!.PlanId;
            var renewalPlan = await _dbContext.MembershipPlans
                .FirstOrDefaultAsync(p => p.Id == planId && p.TenantId == tenantId && p.IsActive);

            if (renewalPlan == null)
                return Result<MembershipDto>.Failure(
                    "Membership plan not found or inactive / الخطة غير موجودة أو غير نشطة");

            var (newStartDate, newEndDate) = MembershipRenewalDating.Calculate(
                coveringMembership ?? planSource, renewalPlan, transitionMode, today);

            var isCash = IsCashPayment(request.PaymentMethod);
            Guid? shiftId = null;
            AppUser? staffUser = null;
            if (isCash)
            {
                var cashSetup = await ResolveCashStaffAndShiftAsync(staffUserId, tenantId, amountRequiresShift: request.AmountPaid > 0);
                if (!cashSetup.IsSuccess)
                    return Result<MembershipDto>.Failure(cashSetup.Error!, cashSetup.Message);
                staffUser = cashSetup.Data!.StaffUser;
                shiftId = cashSetup.Data.ShiftId;
            }

            var renewedMembership = new Membership
            {
                TenantId = tenantId,
                MemberId = memberId,
                PlanId = planId,
                StartDate = newStartDate,
                EndDate = newEndDate,
                Status = isCash ? "active" : "pending",
                SessionsRemaining = renewalPlan.PlanType == "session_pack" ? renewalPlan.SessionCount : null,
                PaymentMethod = request.PaymentMethod,
                AmountPaid = request.AmountPaid,
                PaymentDate = isCash ? DateTime.UtcNow : null,
                AutoRenew = planSource?.AutoRenew ?? false,
                LastRenewalDate = DateTime.UtcNow,
                PlanTransitionMode = transitionMode,
                CreatedAtUtc = DateTime.UtcNow
            };

            // Cash / paid: apply prior expiry per transition mode.
            // Gateway pending: bake dates now but do not clip/expire covering until payment clears.
            if (isCash)
            {
                MembershipRenewalDating.ApplyPriorOpenHandling(
                    allMemberships, renewedMembership.Id, transitionMode, today, apply: true);
            }
            else if (coveringMembership == null
                     && planSource != null
                     && planSource.Status == "active"
                     && planSource.EndDate <= today)
            {
                planSource.Status = "expired";
                planSource.UpdatedAtUtc = DateTime.UtcNow;
            }

            if (isCash)
                await SyncMemberInvitationQuotaAsync(member, renewalPlan);

            if (isCash && staffUser != null)
            {
                await PersistPaidMembershipAsync(
                    member, renewedMembership, renewalPlan, staffUser, shiftId, staffUserId, tenantId,
                    amountPaid: request.AmountPaid, paymentMethod: request.PaymentMethod);

                _logger.LogInformation(
                    "Membership renewed (cash+sale): MemberId={MemberId}, Mode={Mode}, NewStart={Start}, NewEnd={End}, ShiftId={ShiftId}",
                    memberId, transitionMode, newStartDate, newEndDate, shiftId);
            }
            else
            {
                await _membershipRepository.AddAsync(renewedMembership);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation(
                    "Membership renewed (pending): MemberId={MemberId}, Mode={Mode}, NewStart={Start}, NewEnd={End}, PaymentMethod={PaymentMethod}",
                    memberId, transitionMode, newStartDate, newEndDate, request.PaymentMethod);
            }

            await _auditService.LogAsync(
                "membership.renew",
                "Membership",
                renewedMembership.Id,
                before: operationalPrior == null ? null : new
                {
                    membershipId = operationalPrior.Id,
                    planId = operationalPrior.PlanId,
                    status = operationalPrior.Status,
                    startDate = operationalPrior.StartDate,
                    endDate = operationalPrior.EndDate
                },
                after: new
                {
                    membershipId = renewedMembership.Id,
                    planId = renewedMembership.PlanId,
                    transitionMode,
                    startDate = renewedMembership.StartDate,
                    endDate = renewedMembership.EndDate,
                    paymentMethod = renewedMembership.PaymentMethod,
                    amountPaid = renewedMembership.AmountPaid
                });

            return await GetCurrentMembershipAsync(memberId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renewing membership for member {MemberId}", memberId);
            return Result<MembershipDto>.Failure(
                "Failed to renew membership / فشل في تجديد العضوية",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<MembershipDto>> CancelMembershipAsync(
        Guid tenantId, Guid memberId, Guid staffUserId)
    {
        try
        {
            var member = await _dbContext.GymMembers
                .FirstOrDefaultAsync(m => m.Id == memberId && m.TenantId == tenantId);
            if (member == null)
                return Result<MembershipDto>.Failure(
                    "Member not found / العضو غير موجود");

            var memberships = await _dbContext.Memberships
                .Include(m => m.Plan)
                .Where(m => m.MemberId == memberId && m.TenantId == tenantId)
                .ToListAsync();

            var today = MembershipOperational.TodayCairo();
            foreach (var m in memberships)
                MembershipOperational.TryMarkExpired(m, today);

            var target = MembershipOperational.SelectOperational(memberships, today);
            if (target == null)
                return Result<MembershipDto>.Failure(
                    "No membership found for this member / لا توجد عضوية لهذا العضو");

            var effective = MembershipOperational.GetEffectiveStatus(target, today);
            if (effective is not ("active" or "frozen" or "scheduled" or "pending"))
            {
                return Result<MembershipDto>.Failure(
                    "This membership cannot be cancelled / لا يمكن إلغاء هذه العضوية",
                    effective is "expired" or "cancelled"
                        ? "Expired or already cancelled plans are stopped. Renew or Assign to start a new period."
                        : $"Current status is {effective}.");
            }

            var priorStatus = target.Status;
            target.Status = "cancelled";
            target.UpdatedAtUtc = DateTime.UtcNow;
            member.InvitationQuotaRemaining = 0;
            member.UpdatedAtUtc = DateTime.UtcNow;

            var saleId = await _dbContext.SaleLines
                .Where(l => l.TenantId == tenantId
                    && l.LineType == "membership"
                    && l.ReferenceId == target.Id)
                .Select(l => l.SaleId)
                .FirstOrDefaultAsync();
            if (saleId != Guid.Empty)
            {
                var sale = await _dbContext.Sales
                    .FirstOrDefaultAsync(s => s.Id == saleId && s.TenantId == tenantId);
                if (sale != null
                    && sale.Status == "partially_paid"
                    && sale.AmountDue > 0)
                {
                    sale.AmountDue = 0m;
                    sale.DueDate = null;
                }
            }

            await _dbContext.SaveChangesAsync();

            try
            {
                await _auditService.LogAsync(
                    "membership.cancel",
                    "Membership",
                    target.Id,
                    before: new { status = priorStatus, membershipId = target.Id },
                    after: new { status = "cancelled", membershipId = target.Id });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit failed for membership cancel {MembershipId}", target.Id);
            }

            _logger.LogInformation(
                "Membership cancelled (not a refund): MemberId={MemberId}, MembershipId={MembershipId}, Prior={Prior}",
                memberId, target.Id, priorStatus);

            return await GetCurrentMembershipAsync(memberId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling membership for member {MemberId}", memberId);
            return Result<MembershipDto>.Failure(
                "Failed to cancel membership / فشل في إلغاء العضوية",
                ex.Message);
        }
    }

    private static bool IsCashPayment(string? method)
        => string.Equals(method, "cash", StringComparison.OrdinalIgnoreCase);

    private async Task SyncMemberInvitationQuotaAsync(GymMember member, MembershipPlan plan)
    {
        // Denormalized remaining = full plan quota for the new period; detail reads recompute vs usage.
        member.InvitationQuotaRemaining = Math.Max(0, plan.ReferralInviteQuota);
        member.UpdatedAtUtc = DateTime.UtcNow;
        await Task.CompletedTask;
    }

    private sealed class CashStaffShift
    {
        public required AppUser StaffUser { get; init; }
        public Guid? ShiftId { get; init; }
    }

    private async Task<Result<CashStaffShift>> ResolveCashStaffAndShiftAsync(
        Guid staffUserId, Guid tenantId, bool amountRequiresShift)
    {
        var staffUserIdStr = staffUserId.ToString();
        var staffUser = await _dbContext.AppUsers
            .FirstOrDefaultAsync(u => u.UserId == staffUserIdStr && u.TenantId == tenantId);

        if (staffUser == null)
            return Result<CashStaffShift>.Failure(
                $"{SaleFailureReasons.StaffUserNotFound}|Staff user not found / المستخدم غير موجود");

        var shiftId = await _shiftService.GetCurrentOpenShiftIdAsync(staffUserId, tenantId);
        if (amountRequiresShift && shiftId == null)
            return Result<CashStaffShift>.Failure(
                $"{SaleFailureReasons.OpenShiftRequired}|An open shift is required to accept cash payments / يجب فتح وردية لقبول مدفوعات نقدية");

        return Result<CashStaffShift>.Success(new CashStaffShift
        {
            StaffUser = staffUser,
            ShiftId = shiftId
        });
    }

    private async Task<Guid> PersistPaidMembershipAsync(
        GymMember member,
        Membership membership,
        MembershipPlan plan,
        AppUser staffUser,
        Guid? shiftId,
        Guid staffUserIdJwt,
        Guid tenantId,
        decimal amountPaid,
        string paymentMethod)
    {
        // Sale total is the plan price (or cash if they overpay). Paying less leaves
        // AmountDue so Member 360 Outstanding / GET /debtors can see the remainder.
        var saleTotal = Math.Max(plan.Price, amountPaid);
        var amountDue = Math.Max(0m, saleTotal - amountPaid);
        var hasDue = amountDue > 0m;

        var sale = new Sale
        {
            TenantId = tenantId,
            MemberId = member.Id,
            SoldByUserId = staffUser.Id,
            ShiftId = shiftId,
            Subtotal = saleTotal,
            DiscountAmount = 0m,
            TaxAmount = 0m,
            Total = saleTotal,
            AmountDue = amountDue,
            DueDate = hasDue ? MembershipOperational.TodayCairo() : null,
            Status = hasDue ? "partially_paid" : "completed"
        };
        _dbContext.Sales.Add(sale);
        _dbContext.Memberships.Add(membership);

        _dbContext.SaleLines.Add(new SaleLine
        {
            TenantId = tenantId,
            SaleId = sale.Id,
            LineType = "membership",
            ReferenceId = membership.Id,
            Description = plan.Name,
            DescriptionAr = plan.NameAr,
            Qty = 1,
            UnitPrice = saleTotal,
            LineTotal = saleTotal
        });

        if (amountPaid > 0m)
        {
            _dbContext.PaymentTransactions.Add(new PaymentTransaction
            {
                TenantId = tenantId,
                MemberId = member.Id,
                MembershipId = membership.Id,
                Gateway = paymentMethod,
                ExternalRef = $"MEMBERSHIP:{membership.Id}",
                Amount = amountPaid,
                Currency = "EGP",
                Status = "success",
                PaidAtUtc = DateTime.UtcNow,
                SaleId = sale.Id,
                ReceivedByUserId = staffUser.Id,
                ShiftId = shiftId,
                Method = paymentMethod,
                SettlementStatus = IsCashPayment(paymentMethod) ? "settled" : "pending",
                SettledAtUtc = IsCashPayment(paymentMethod) ? DateTime.UtcNow : null
            });
        }

        await _dbContext.SaveChangesAsync();

        try
        {
            await _invoiceService.EnqueueForSale(sale.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enqueue invoice for membership sale {SaleId}", sale.Id);
        }

        if (shiftId.HasValue && amountPaid > 0 && IsCashPayment(paymentMethod))
        {
            try
            {
                await _shiftService.RecordMovementAsync(
                    shiftId.Value, "sale", amountPaid, sale.Id, null, staffUserIdJwt, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to record cash movement for membership sale {SaleId} on shift {ShiftId}",
                    sale.Id, shiftId);
            }
        }

        return sale.Id;
    }


    private async Task<MembershipDto> MapToDtoAsync(Membership membership)
    {
        var dto = new MembershipDto
        {
            Id = membership.Id,
            PlanName = membership.Plan?.Name ?? "Unknown",
            PlanNameAr = membership.Plan?.NameAr ?? "غير معروف",
            PlanType = membership.Plan?.PlanType ?? string.Empty,
            StartDate = membership.StartDate,
            EndDate = membership.EndDate,
            Status = MembershipOperational.GetEffectiveStatus(membership),
            SessionsRemaining = membership.SessionsRemaining,
            SessionCount = membership.Plan?.SessionCount,
            AmountPaid = membership.AmountPaid,
            PaymentMethod = membership.PaymentMethod,
            PaymentDate = membership.PaymentDate,
            AutoRenew = membership.AutoRenew,
            FrozenFromDate = membership.FrozenFromDate,
            FrozenUntilDate = membership.FrozenUntilDate
        };

        if (_tenantContext.IsInitialized)
        {
            dto.ActivityQuotas = await _activityEntitlements.ListQuotasForMembershipAsync(
                _tenantContext.TenantId, membership.MemberId, membership);
        }

        return dto;
    }
}
