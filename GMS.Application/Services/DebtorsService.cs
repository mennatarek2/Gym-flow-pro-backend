namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Debtors;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Front-desk debtors list: aggregates each member's outstanding balance across their
/// partially_paid sales, with an aging bucket and a throttled WhatsApp reminder action.
/// </summary>
public class DebtorsService : IDebtorsService
{
    private static readonly TimeSpan ReminderThrottleTtl = TimeSpan.FromHours(48);

    private readonly GymFlowProDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IPaymobService _paymobService;
    private readonly ILogger<DebtorsService> _logger;

    public DebtorsService(
        GymFlowProDbContext dbContext, IDistributedCache cache, IWhatsAppService whatsAppService,
        IPaymobService paymobService, ILogger<DebtorsService> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
        _whatsAppService = whatsAppService;
        _paymobService = paymobService;
        _logger = logger;
    }

    public async Task<Result<List<DebtorDto>>> GetAllDebtorsAsync(Guid tenantId)
    {
        try
        {
            var debtors = await ComputeDebtorsAsync(tenantId, memberId: null);
            return Result<List<DebtorDto>>.Success(debtors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving debtors for tenant {TenantId}", tenantId);
            return Result<List<DebtorDto>>.Failure("Failed to retrieve debtors / فشل جلب قائمة المدينين", ex.Message);
        }
    }

    public async Task<Result<PagedResult<DebtorDto>>> GetDebtorsPagedAsync(Guid tenantId, int page, int pageSize, Guid? memberId = null)
    {
        try
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var debtors = await ComputeDebtorsAsync(tenantId, memberId);

            var items = debtors.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Result<PagedResult<DebtorDto>>.Success(new PagedResult<DebtorDto>
            {
                Items = items,
                TotalCount = debtors.Count,
                Page = page,
                PageSize = pageSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving debtors for tenant {TenantId}", tenantId);
            return Result<PagedResult<DebtorDto>>.Failure("Failed to retrieve debtors / فشل جلب قائمة المدينين", ex.Message);
        }
    }

    public async Task<Result<DebtorsSummaryDto>> GetSummaryAsync(Guid tenantId)
    {
        try
        {
            var debtors = await ComputeDebtorsAsync(tenantId, memberId: null);

            return Result<DebtorsSummaryDto>.Success(new DebtorsSummaryDto
            {
                TotalOutstanding = debtors.Sum(d => d.TotalDue),
                DebtorCount = debtors.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving debtor summary for tenant {TenantId}", tenantId);
            return Result<DebtorsSummaryDto>.Failure("Failed to retrieve debtor summary / فشل جلب ملخص المديونيات", ex.Message);
        }
    }

    public async Task<Result<MemberOutstandingSalesDto>> GetOutstandingSalesAsync(Guid tenantId, Guid memberId)
    {
        try
        {
            var sales = await _dbContext.Sales
                .Include(s => s.Lines)
                .Where(s => s.TenantId == tenantId
                            && s.MemberId == memberId
                            && s.Status == "partially_paid"
                            && s.AmountDue > 0)
                .OrderBy(s => s.DueDate)
                .ThenBy(s => s.CreatedAtUtc)
                .ToListAsync();

            return Result<MemberOutstandingSalesDto>.Success(new MemberOutstandingSalesDto
            {
                MemberId = memberId,
                TotalDue = sales.Sum(s => s.AmountDue),
                Sales = sales.Select(s => new OutstandingSaleDto
                {
                    SaleId = s.Id,
                    CreatedAtUtc = s.CreatedAtUtc,
                    DueDate = s.DueDate,
                    Status = s.Status,
                    Description = s.Lines
                        .OrderBy(l => l.CreatedAtUtc)
                        .Select(l => l.Description)
                        .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)) ?? "Sale",
                    Total = s.Total,
                    Paid = s.Total - s.AmountDue,
                    AmountDue = s.AmountDue
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving outstanding sales for member {MemberId}", memberId);
            return Result<MemberOutstandingSalesDto>.Failure(
                "Failed to retrieve outstanding sales / فشل جلب المبيعات المستحقة", ex.Message);
        }
    }

    public async Task<Result<bool>> RemindAsync(Guid memberId, Guid tenantId, Guid staffUserId)
    {
        try
        {
            var throttleKey = BuildThrottleKey(tenantId, memberId);
            var alreadySent = await _cache.GetStringAsync(throttleKey);
            if (!string.IsNullOrEmpty(alreadySent))
                return Fail(DebtorFailureReasons.ReminderThrottle,
                    "A reminder was already sent to this member recently / تم إرسال تذكير لهذا العضو مؤخرًا");

            var member = await _dbContext.GymMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.TenantId == tenantId);
            if (member == null)
                return Fail(DebtorFailureReasons.MemberNotFound, "Member not found / العضو غير موجود");

            var oldestSale = await _dbContext.Sales
                .Where(s => s.TenantId == tenantId && s.MemberId == memberId && s.Status == "partially_paid" && s.AmountDue > 0)
                .OrderBy(s => s.DueDate)
                .FirstOrDefaultAsync();

            if (oldestSale == null)
                return Fail(DebtorFailureReasons.NoOutstandingBalance,
                    "This member has no outstanding balance / لا يوجد رصيد مستحق لهذا العضو");

            var paymentLink = oldestSale.AmountDue.ToString("F2");

            var membershipId = await _dbContext.SaleLines
                .Where(l => l.SaleId == oldestSale.Id && l.TenantId == tenantId && l.LineType == "membership")
                .Select(l => (Guid?)l.ReferenceId)
                .FirstOrDefaultAsync();

            if (membershipId.HasValue)
            {
                try
                {
                    paymentLink = await _paymobService.CreatePaymentIntentAsync(
                        oldestSale.Id, memberId, tenantId, oldestSale.AmountDue, member.PhoneNumber);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to create a Paymob payment intent for member {MemberId}'s reminder — falling back to a plain amount",
                        memberId);
                }
            }

            await _whatsAppService.SendTemplateAsync(member.PhoneNumber, "payment_reminder", new Dictionary<string, string>
            {
                ["memberName"] = member.FullName,
                ["amount"] = oldestSale.AmountDue.ToString("F2"),
                ["dueDate"] = oldestSale.DueDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                ["paymentLink"] = paymentLink
            });

            await _cache.SetStringAsync(throttleKey, "1",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ReminderThrottleTtl });

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending payment reminder for member {MemberId}", memberId);
            return Result<bool>.Failure("Failed to send payment reminder / فشل إرسال تذكير الدفع", ex.Message);
        }
    }

    // ========================================================================
    // PRIVATE HELPERS
    // ========================================================================

    private async Task<List<DebtorDto>> ComputeDebtorsAsync(Guid tenantId, Guid? memberId)
    {
        var sales = await _dbContext.Sales
            .Where(s => s.TenantId == tenantId
                && s.Status == "partially_paid"
                && s.AmountDue > 0
                && (!memberId.HasValue || s.MemberId == memberId.Value))
            .Select(s => new
            {
                s.MemberId,
                s.GuestName,
                s.GuestPhone,
                s.AmountDue,
                s.DueDate
            })
            .ToListAsync();

        var groups = sales
            .GroupBy(s => new { s.MemberId, s.GuestName, s.GuestPhone })
            .Select(g => new
            {
                g.Key.MemberId,
                g.Key.GuestName,
                g.Key.GuestPhone,
                TotalDue = g.Sum(s => s.AmountDue),
                OldestDueDate = g.Min(s => s.DueDate)
            })
            .ToList();

        if (groups.Count == 0)
            return new List<DebtorDto>();

        var memberIds = groups
            .Where(g => g.MemberId.HasValue)
            .Select(g => g.MemberId!.Value)
            .Distinct()
            .ToList();

        var members = await _dbContext.GymMembers
            .Where(m => memberIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id);

        var lastPayments = await _dbContext.PaymentTransactions
            .Where(p => p.TenantId == tenantId && p.MemberId != null && memberIds.Contains(p.MemberId.Value))
            .GroupBy(p => p.MemberId!.Value)
            .Select(g => new { MemberId = g.Key, LastPaymentAt = g.Max(p => p.PaidAtUtc) })
            .ToDictionaryAsync(g => g.MemberId, g => g.LastPaymentAt);

        var today = MembershipOperational.TodayCairo();

        var debtors = new List<(DebtorDto Dto, int DaysSinceDue)>();

        foreach (var group in groups)
        {
            var oldestDueDate = group.OldestDueDate ?? today;
            var daysSinceDue = today.DayNumber - oldestDueDate.DayNumber;
            GymMember? member = null;
            var hasMember = group.MemberId.HasValue
                && members.TryGetValue(group.MemberId.Value, out member);
            var fullName = hasMember ? member!.FullName : group.GuestName ?? "Guest sale";
            var phone = hasMember ? member!.PhoneNumber : group.GuestPhone ?? string.Empty;

            var dto = new DebtorDto
            {
                MemberId = group.MemberId,
                GuestName = hasMember ? null : group.GuestName,
                FullName = fullName,
                PhoneNumber = phone,
                TotalDue = group.TotalDue,
                OldestDueDate = oldestDueDate,
                AgingBucket = ComputeAgingBucket(daysSinceDue),
                LastPaymentAt = group.MemberId.HasValue
                    && lastPayments.TryGetValue(group.MemberId.Value, out var lastPaymentAt)
                    ? lastPaymentAt
                    : null
            };

            debtors.Add((dto, daysSinceDue));
        }

        // Oldest first — largest days-since-due first (equivalent to "AgingBucket DESC").
        return debtors.OrderByDescending(d => d.DaysSinceDue).Select(d => d.Dto).ToList();
    }

    /// <summary>0-7 / 8-30 / 30+ days elapsed since the due date.</summary>
    public static string ComputeAgingBucket(int daysSinceDue) => daysSinceDue switch
    {
        <= 7 => "0-7",
        <= 30 => "8-30",
        _ => "30+"
    };

    private static string BuildThrottleKey(Guid tenantId, Guid memberId) => $"reminder_throttle:{tenantId}:{memberId}";

    private static Result<bool> Fail(string code, string message) => Result<bool>.Failure($"{code}|{message}");
}
