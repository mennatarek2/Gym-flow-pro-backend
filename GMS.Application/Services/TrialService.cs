namespace GMS.Application.Services;

using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Members;
using GMS.Application.DTOs.Trials;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Free-trial issuance: staff-initiated, phone-OTP-verified two-step flow. Pending signup data
/// (not yet a real member) is held in Redis for 10 minutes between initiate and confirm — nothing
/// is written to the database until the OTP is confirmed.
/// </summary>
public class TrialService : ITrialService
{
    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
    private static readonly TimeSpan PendingTrialTtl = TimeSpan.FromMinutes(10);

    private readonly GymFlowProDbContext _dbContext;
    private readonly IMemberRepository _memberRepo;
    private readonly IOtpService _otpService;
    private readonly IDistributedCache _cache;
    private readonly IInvoiceService _invoiceService;
    private readonly ITierEnforcementService _tierEnforcement;
    private readonly ILogger<TrialService> _logger;

    public TrialService(
        GymFlowProDbContext dbContext,
        IMemberRepository memberRepo,
        IOtpService otpService,
        IDistributedCache cache,
        IInvoiceService invoiceService,
        ITierEnforcementService tierEnforcement,
        ILogger<TrialService> logger)
    {
        _dbContext = dbContext;
        _memberRepo = memberRepo;
        _otpService = otpService;
        _cache = cache;
        _invoiceService = invoiceService;
        _tierEnforcement = tierEnforcement;
        _logger = logger;
    }

    public async Task<Result<TrialInitiateResponse>> InitiateAsync(TrialInitiateRequest request, Guid tenantId)
    {
        try
        {
            var plan = await _dbContext.MembershipPlans
                .FirstOrDefaultAsync(p => p.Id == request.PlanId && p.TenantId == tenantId && p.IsActive);

            if (plan == null)
                return Fail<TrialInitiateResponse>(TrialFailureReasons.PlanNotFound, "Membership plan not found or inactive / الخطة غير موجودة أو غير نشطة");

            if (plan.PlanType != "trial")
                return Fail<TrialInitiateResponse>(TrialFailureReasons.PlanNotTrial, "This plan is not a trial plan / هذه الخطة ليست خطة تجريبية");

            var normalizedPhone = NormalizePhoneNumber(request.PhoneNumber);

            // Anti-abuse: has this phone ever been used for a trial at this tenant before?
            var hasPriorTrial = await _dbContext.Memberships
                .AnyAsync(m => m.TenantId == tenantId
                            && m.Plan!.PlanType == "trial"
                            && m.Member!.PhoneNumber == normalizedPhone);

            if (hasPriorTrial)
                return Fail<TrialInitiateResponse>(TrialFailureReasons.TrialAlreadyUsed,
                    "This phone number has already used a free trial / رقم الهاتف هذا استخدم تجربة مجانية بالفعل");

            await _otpService.GenerateOtpAsync(normalizedPhone, tenantId);

            var pending = new PendingTrialData
            {
                FullName = request.FullName,
                FullNameAr = request.FullNameAr,
                PhoneNumber = normalizedPhone,
                PlanId = request.PlanId
            };

            await _cache.SetStringAsync(
                BuildPendingKey(tenantId, normalizedPhone),
                JsonSerializer.Serialize(pending),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = PendingTrialTtl });

            return Result<TrialInitiateResponse>.Success(new TrialInitiateResponse
            {
                OtpSent = true,
                ExpiresInSeconds = (int)PendingTrialTtl.TotalSeconds
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating trial for tenant {TenantId}", tenantId);
            return Result<TrialInitiateResponse>.Failure("Failed to initiate trial / فشل بدء التجربة المجانية", ex.Message);
        }
    }

    public async Task<Result<TrialConfirmResponse>> ConfirmAsync(TrialConfirmRequest request, Guid staffUserId, Guid tenantId)
    {
        var isRelational = _dbContext.Database.IsRelational();
        var transaction = isRelational
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
            : null;

        try
        {
            var normalizedPhone = NormalizePhoneNumber(request.PhoneNumber);

            var otpValid = await _otpService.ValidateOtpAsync(normalizedPhone, tenantId, request.Otp);
            if (!otpValid)
            {
                if (transaction != null) await transaction.RollbackAsync();
                return Fail<TrialConfirmResponse>(TrialFailureReasons.OtpInvalid, "Invalid or expired OTP / رمز التحقق غير صالح أو منتهي الصلاحية");
            }

            var pendingJson = await _cache.GetStringAsync(BuildPendingKey(tenantId, normalizedPhone));
            if (string.IsNullOrEmpty(pendingJson))
            {
                if (transaction != null) await transaction.RollbackAsync();
                return Fail<TrialConfirmResponse>(TrialFailureReasons.PendingTrialNotFound,
                    "No pending trial signup found for this phone — please initiate again / لا يوجد طلب تجربة معلق لهذا الرقم، يرجى البدء من جديد");
            }

            var pending = JsonSerializer.Deserialize<PendingTrialData>(pendingJson)!;

            var staffUserIdStr = staffUserId.ToString();
            var staffUser = await _dbContext.AppUsers
                .FirstOrDefaultAsync(u => u.UserId == staffUserIdStr && u.TenantId == tenantId);

            if (staffUser == null)
            {
                if (transaction != null) await transaction.RollbackAsync();
                return Fail<TrialConfirmResponse>(TrialFailureReasons.StaffUserNotFound, "Staff user not found / المستخدم غير موجود");
            }

            var plan = await _dbContext.MembershipPlans
                .FirstOrDefaultAsync(p => p.Id == pending.PlanId && p.TenantId == tenantId && p.IsActive);

            if (plan == null || plan.PlanType != "trial")
            {
                if (transaction != null) await transaction.RollbackAsync();
                return Fail<TrialConfirmResponse>(TrialFailureReasons.PlanNotTrial, "Trial plan is no longer available / الخطة التجريبية لم تعد متاحة");
            }

            // Soft cap only — trial confirm bypasses MemberService; never block.
            var capCheck = await _tierEnforcement.CheckCapAsync(tenantId, "active_members");
            var softCapMessage = capCheck.SoftWarning ? "PLAN_SOFT_CAP:active_members" : null;

            var sequence = await _memberRepo.GetNextMemberSequenceAsync(tenantId);
            var today = GetTodayCairo();

            var member = new GymMember
            {
                TenantId = tenantId,
                MemberNumber = $"GYM-{sequence:D3}",
                FullName = pending.FullName.Trim(),
                FullNameAr = pending.FullNameAr?.Trim() ?? string.Empty,
                PhoneNumber = pending.PhoneNumber,
                DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-20)),
                NationalIdEncrypted = string.Empty,
                IsActive = true,
                IsTrial = true,
                TrialOutcome = "active_trial"
            };
            _dbContext.GymMembers.Add(member);

            var membership = new Membership
            {
                TenantId = tenantId,
                MemberId = member.Id,
                PlanId = plan.Id,
                StartDate = today,
                EndDate = plan.PlanType == "day_pass" ? today : today.AddDays(plan.DurationDays),
                Status = "active",
                SessionsRemaining = plan.PlanType == "session_pack" ? plan.SessionCount : null,
                PaymentMethod = "trial",
                AmountPaid = 0m,
                PaymentDate = DateTime.UtcNow
            };
            _dbContext.Memberships.Add(membership);

            var sale = new Sale
            {
                TenantId = tenantId,
                MemberId = member.Id,
                SoldByUserId = staffUser.Id,
                Subtotal = 0m,
                DiscountAmount = 0m,
                TaxAmount = 0m,
                Total = 0m,
                AmountDue = 0m,
                Status = "completed"
            };
            _dbContext.Sales.Add(sale);

            _dbContext.SaleLines.Add(new SaleLine
            {
                TenantId = tenantId,
                SaleId = sale.Id,
                LineType = "trial",
                ReferenceId = membership.Id,
                Description = plan.Name,
                DescriptionAr = plan.NameAr,
                Qty = 1,
                UnitPrice = 0m,
                LineTotal = 0m
            });

            await _dbContext.SaveChangesAsync();
            if (transaction != null)
                await transaction.CommitAsync();

            await _cache.RemoveAsync(BuildPendingKey(tenantId, normalizedPhone));

            // Zero-total sale — EnqueueForSale's own check skips it, no invoice job is queued.
            await _invoiceService.EnqueueForSale(sale.Id);

            _logger.LogInformation(
                "Trial issued: member {MemberNumber} on plan {PlanId} for tenant {TenantId}",
                member.MemberNumber, plan.Id, tenantId);

            return Result<TrialConfirmResponse>.Success(new TrialConfirmResponse
            {
                Member = MapToMemberDto(member),
                Membership = MapToMembershipDto(membership, plan)
            }, softCapMessage);
        }
        catch (Exception ex)
        {
            if (transaction != null)
                await transaction.RollbackAsync();

            _logger.LogError(ex, "Error confirming trial for tenant {TenantId}", tenantId);
            return Result<TrialConfirmResponse>.Failure("Failed to confirm trial / فشل تأكيد التجربة المجانية", ex.Message);
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    // ========================================================================
    // PRIVATE HELPERS
    // ========================================================================

    /// <summary>
    /// Normalizes an Egyptian phone number to "+20XXXXXXXXXX". Neither MemberService nor
    /// OtpService normalize phone numbers today, so this is new logic local to trial issuance.
    /// </summary>
    private static string NormalizePhoneNumber(string phone)
    {
        var digitsOnly = new string(phone.Where(char.IsDigit).ToArray());

        if (digitsOnly.StartsWith("20") && digitsOnly.Length == 12)
            return "+" + digitsOnly;

        if (digitsOnly.StartsWith("0") && digitsOnly.Length == 11)
            return "+20" + digitsOnly[1..];

        if (digitsOnly.Length == 10)
            return "+20" + digitsOnly;

        return "+" + digitsOnly;
    }

    private static string BuildPendingKey(Guid tenantId, string normalizedPhone) => $"trial_pending:{tenantId}:{normalizedPhone}";

    private static DateOnly GetTodayCairo() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CairoTimeZone));

    private static Result<T> Fail<T>(string code, string message) =>
        Result<T>.Failure($"{code}|{message}");

    private static MemberDetailDto MapToMemberDto(GymMember m) => new()
    {
        Id = m.Id,
        MemberNumber = m.MemberNumber,
        FullName = m.FullName,
        FullNameAr = m.FullNameAr,
        Phone = m.PhoneNumber,
        Email = m.Email,
        DateOfBirth = m.DateOfBirth,
        ProfilePhotoUrl = m.ProfilePhotoUrl,
        Notes = m.Notes,
        IsActive = m.IsActive,
        InvitationQuotaRemaining = m.InvitationQuotaRemaining,
        CreatedAtUtc = m.CreatedAtUtc,
        CurrentMembership = null,
        RecentAttendance = new List<AttendanceSummaryDto>()
    };

    private static MembershipSummaryDto MapToMembershipDto(Membership ms, MembershipPlan plan) => new()
    {
        Id = ms.Id,
        PlanName = plan.Name,
        PlanNameAr = plan.NameAr,
        PlanType = plan.PlanType,
        Status = ms.Status,
        StartDate = ms.StartDate,
        EndDate = ms.EndDate,
        SessionsRemaining = ms.SessionsRemaining,
        FrozenFromDate = ms.FrozenFromDate,
        FrozenUntilDate = ms.FrozenUntilDate,
        AmountPaid = ms.AmountPaid,
        PaymentMethod = ms.PaymentMethod
    };

    private class PendingTrialData
    {
        public string FullName { get; set; } = string.Empty;
        public string? FullNameAr { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public Guid PlanId { get; set; }
    }
}
