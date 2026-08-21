namespace GMS.Application.Services;

using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using GMS.Application.Common;
using GMS.Application.DTOs.Analytics;
using GMS.Application.DTOs.Reports;
using GMS.Application.Interfaces;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Reports service implementation.
/// Queries real-time data for detailed reporting.
/// </summary>
public class ReportsService : IReportsService
{
    private const int MaxRangeDays = 90;
    private const int ListCap = 500;

    private readonly GymFlowProDbContext _dbContext;
    private readonly IInventoryReportService _inventoryReports;
    private readonly ILogger<ReportsService> _logger;

    public ReportsService(
        GymFlowProDbContext dbContext,
        IInventoryReportService inventoryReports,
        ILogger<ReportsService> logger)
    {
        _dbContext = dbContext;
        _inventoryReports = inventoryReports;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<List<AttendanceSummaryItemDto>>> GetAttendanceSummaryAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate)
    {
        try
        {
            var fromDateTime = fromDate.ToDateTime(TimeOnly.MinValue);
            var toDateTime = toDate.ToDateTime(new TimeOnly(23, 59, 59));

            var summary = await _dbContext.GymAttendances
                .Where(a => a.TenantId == tenantId && a.CheckInAtUtc >= fromDateTime && a.CheckInAtUtc <= toDateTime)
                .GroupBy(a => DateOnly.FromDateTime(a.CheckInAtUtc))
                .Select(g => new AttendanceSummaryItemDto
                {
                    Date = g.Key,
                    CheckinCount = g.Count(),
                    UniqueMembers = g.Select(a => a.MemberId).Distinct().Count()
                })
                .OrderBy(x => x.Date)
                .ToListAsync();

            return Result<List<AttendanceSummaryItemDto>>.Success(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting attendance summary for tenant {TenantId}", tenantId);
            return Result<List<AttendanceSummaryItemDto>>.Failure("Failed to get attendance summary");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<List<RevenueDetailItemDto>>> GetRevenueDetailAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate, string? paymentMethod = null)
    {
        try
        {
            var fromDateTime = fromDate.ToDateTime(TimeOnly.MinValue);
            var toDateTime = toDate.ToDateTime(new TimeOnly(23, 59, 59));

            var baseQuery = _dbContext.Memberships
                .Where(m => m.TenantId == tenantId && 
                           m.PaymentDate.HasValue && 
                           m.PaymentDate.Value >= fromDateTime && 
                           m.PaymentDate.Value <= toDateTime);

            if (!string.IsNullOrEmpty(paymentMethod))
            {
                baseQuery = baseQuery.Where(m => m.PaymentMethod == paymentMethod);
            }

            var revenue = await baseQuery
                .Include(m => m.Member)
                .Include(m => m.Plan)
                .Select(m => new RevenueDetailItemDto
                {
                    Id = m.Id,
                    TransactionDate = m.PaymentDate!.Value,
                    MemberName = m.Member!.FullName,
                    PlanName = m.Plan!.Name,
                    Amount = m.Plan.Price,
                    PaymentMethod = m.PaymentMethod
                })
                .OrderByDescending(x => x.TransactionDate)
                .ToListAsync();

            return Result<List<RevenueDetailItemDto>>.Success(revenue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting revenue detail for tenant {TenantId}", tenantId);
            return Result<List<RevenueDetailItemDto>>.Failure("Failed to get revenue detail");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<List<PeakHourItemDto>>> GetPeakHoursAsync(Guid tenantId)
    {
        try
        {
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

            // CheckInAtUtc.Hour is not translated inside GroupBy on SQL Server — aggregate in memory.
            var checkInTimes = await _dbContext.GymAttendances
                .Where(a => a.TenantId == tenantId && a.CheckInAtUtc >= thirtyDaysAgo)
                .Select(a => a.CheckInAtUtc)
                .ToListAsync();

            var peakHours = checkInTimes
                .GroupBy(t => t.Hour)
                .Select(g => new { Hour = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToList();

            var totalCheckins = peakHours.Sum(x => x.Count);

            var result = peakHours
                .Select(x => new PeakHourItemDto
                {
                    TimeSlot = $"{x.Hour:D2}:00-{x.Hour + 1:D2}:00",
                    CheckinCount = x.Count,
                    Percentage = totalCheckins > 0 ? (x.Count / (decimal)totalCheckins) * 100 : 0
                })
                .ToList();

            return Result<List<PeakHourItemDto>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting peak hours for tenant {TenantId}", tenantId);
            return Result<List<PeakHourItemDto>>.Failure("Failed to get peak hours");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<MemberRetentionDto>> GetMemberRetentionAsync(Guid tenantId)
    {
        try
        {
            // Get all expired memberships
            var expiredMemberships = await _dbContext.Memberships
                .Where(m => m.TenantId == tenantId && m.Status == "expired")
                .Select(m => m.MemberId)
                .Distinct()
                .ToListAsync();

            // Check how many have renewed (have another membership after current)
            var renewedCount = 0;
            foreach (var memberId in expiredMemberships)
            {
                var hasRenewal = await _dbContext.Memberships
                    .AnyAsync(m => m.MemberId == memberId && m.Status != "expired");
                if (hasRenewal)
                    renewedCount++;
            }

            var retentionRate = expiredMemberships.Count > 0 
                ? (renewedCount / (decimal)expiredMemberships.Count) * 100 
                : 0;

            var dto = new MemberRetentionDto
            {
                TotalExpiredMemberships = expiredMemberships.Count,
                RenewedMemberships = renewedCount,
                RetentionRate = retentionRate
            };

            return Result<MemberRetentionDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting member retention for tenant {TenantId}", tenantId);
            return Result<MemberRetentionDto>.Failure("Failed to get member retention");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<SalesReportDto>> GetSalesReportAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate,
        string? paymentMethod = null, Guid? staffId = null, string? saleType = null)
    {
        var bounds = ValidateRange(fromDate, toDate);
        if (!bounds.ok)
            return Result<SalesReportDto>.Failure(bounds.error!);

        try
        {
            var (utcStart, utcEnd) = MembershipOperational.CairoInclusiveRangeUtc(fromDate, toDate);

            var paymentsAll = await _dbContext.PaymentTransactions.AsNoTracking()
                .Where(p => p.TenantId == tenantId
                         && p.Status == "success"
                         && p.Amount > 0
                         && p.PaidAtUtc >= utcStart
                         && p.PaidAtUtc < utcEnd)
                .Include(p => p.Member)
                .Include(p => p.ReceivedByUser)
                .OrderByDescending(p => p.PaidAtUtc)
                .ToListAsync();

            var staffOptions = paymentsAll
                .GroupBy(p => p.ReceivedByUserId)
                .Select(g => new SalesReportStaffOptionDto
                {
                    UserId = g.Key,
                    Name = FormatStaff(g.Select(p => p.ReceivedByUser).FirstOrDefault())
                })
                .OrderBy(s => s.Name)
                .ToList();

            IEnumerable<PaymentTransaction> filtered = paymentsAll;
            if (staffId.HasValue)
                filtered = filtered.Where(p => p.ReceivedByUserId == staffId.Value);
            if (!string.IsNullOrWhiteSpace(paymentMethod))
                filtered = filtered.Where(p => p.Method == paymentMethod);

            var paymentsList = filtered.ToList();

            var saleIdsFromPay = paymentsList
                .Where(p => p.SaleId.HasValue)
                .Select(p => p.SaleId!.Value)
                .Distinct()
                .ToList();

            var paidSaleLines = saleIdsFromPay.Count == 0
                ? new List<SaleLine>()
                : await _dbContext.SaleLines.AsNoTracking()
                    .Where(l => l.TenantId == tenantId && saleIdsFromPay.Contains(l.SaleId))
                    .ToListAsync();

            var typeBySale = paidSaleLines
                .GroupBy(l => l.SaleId)
                .ToDictionary(g => g.Key, g => ClassifySaleLines(g));

            if (!string.IsNullOrWhiteSpace(saleType))
            {
                paymentsList = paymentsList
                    .Where(p => ClassifyPayment(p, typeBySale) == saleType)
                    .ToList();
                saleIdsFromPay = paymentsList
                    .Where(p => p.SaleId.HasValue)
                    .Select(p => p.SaleId!.Value)
                    .Distinct()
                    .ToList();
            }

            var includeCashRefunds = string.IsNullOrWhiteSpace(paymentMethod) || paymentMethod == "cash";
            decimal cashRefunds = 0m;
            if (includeCashRefunds)
            {
                var refundQuery = _dbContext.Refunds.AsNoTracking()
                    .Where(r => r.TenantId == tenantId
                             && r.Status == "executed"
                             && r.Method == "cash"
                             && r.ExecutedAt != null
                             && r.ExecutedAt >= utcStart
                             && r.ExecutedAt < utcEnd);
                if (staffId.HasValue)
                    refundQuery = refundQuery.Where(r => r.RequestedByUserId == staffId.Value);
                cashRefunds = await refundQuery.SumAsync(r => (decimal?)r.Amount) ?? 0m;
            }

            var createdSaleIds = await _dbContext.Sales.AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.CreatedAtUtc >= utcStart && s.CreatedAtUtc < utcEnd)
                .Select(s => s.Id)
                .ToListAsync();

            var bookedLines = createdSaleIds.Count == 0
                ? new List<SaleLine>()
                : await _dbContext.SaleLines.AsNoTracking()
                    .Where(l => l.TenantId == tenantId && createdSaleIds.Contains(l.SaleId))
                    .ToListAsync();

            var paidSales = saleIdsFromPay.Count == 0
                ? new List<Sale>()
                : await _dbContext.Sales.AsNoTracking()
                    .Where(s => s.TenantId == tenantId && saleIdsFromPay.Contains(s.Id))
                    .ToListAsync();

            var invoices = saleIdsFromPay.Count == 0
                ? new List<Invoice>()
                : await _dbContext.Invoices.AsNoTracking()
                    .Where(i => i.TenantId == tenantId
                             && i.Type == "invoice"
                             && i.SaleId != null
                             && saleIdsFromPay.Contains(i.SaleId.Value))
                    .ToListAsync();

            var invoiceBySale = invoices
                .GroupBy(i => i.SaleId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var cashIn = paymentsList.Sum(p => p.Amount);
            var membershipCashIn = paymentsList
                .Where(p => ClassifyPayment(p, typeBySale) == "membership")
                .Sum(p => p.Amount);
            var productCashIn = paymentsList
                .Where(p => ClassifyPayment(p, typeBySale) == "product")
                .Sum(p => p.Amount);
            var mixedCashIn = paymentsList
                .Where(p => ClassifyPayment(p, typeBySale) == "mixed")
                .Sum(p => p.Amount);

            var daysByCairo = paymentsList
                .GroupBy(p => MembershipOperational.ToCairoDate(p.PaidAtUtc))
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
            var days = new List<SalesReportDayDto>();
            for (var d = fromDate; d <= toDate; d = d.AddDays(1))
            {
                days.Add(new SalesReportDayDto
                {
                    Date = d,
                    CashIn = daysByCairo.TryGetValue(d, out var v) ? v : 0m
                });
            }

            var dto = new SalesReportDto
            {
                From = fromDate,
                To = toDate,
                CashInTotal = cashIn,
                CashRefundsTotal = cashRefunds,
                NetCashIn = cashIn - cashRefunds,
                BookedTotal = bookedLines.Sum(l => l.LineTotal),
                DiscountTotal = paidSales.Sum(s => s.DiscountAmount + (s.ManualDiscountAmount ?? 0m)),
                MembershipCashIn = membershipCashIn,
                ProductCashIn = productCashIn,
                MixedCashIn = mixedCashIn,
                TransactionCount = paymentsList.Count,
                PaymentsTruncated = paymentsList.Count > ListCap,
                Staff = staffOptions,
                MethodOptions = paymentsAll
                    .Select(p => string.IsNullOrEmpty(p.Method) ? "unknown" : p.Method!)
                    .Distinct()
                    .OrderBy(m => m)
                    .ToList(),
                Days = days,
                Methods = paymentsList
                    .GroupBy(p => string.IsNullOrEmpty(p.Method) ? "unknown" : p.Method!)
                    .Select(g => new ReportMethodTotalDto
                    {
                        Method = g.Key,
                        Count = g.Count(),
                        CashIn = g.Sum(p => p.Amount)
                    })
                    .OrderBy(m => m.Method)
                    .ToList(),
                LineTypes = bookedLines
                    .GroupBy(l => l.LineType)
                    .Select(g => new ReportLineTypeTotalDto
                    {
                        LineType = g.Key,
                        Count = g.Count(),
                        Booked = g.Sum(l => l.LineTotal)
                    })
                    .OrderBy(l => l.LineType)
                    .ToList(),
                Payments = paymentsList.Take(ListCap).Select(p =>
                {
                    Invoice? inv = null;
                    if (p.SaleId.HasValue)
                        invoiceBySale.TryGetValue(p.SaleId.Value, out inv);
                    return new SalesReportPaymentRowDto
                    {
                        Id = p.Id,
                        PaidAtUtc = p.PaidAtUtc,
                        Method = p.Method ?? string.Empty,
                        Amount = p.Amount,
                        SaleId = p.SaleId,
                        InvoiceId = inv?.Id,
                        InvoiceNumber = inv?.InvoiceNumber,
                        MemberName = p.Member?.FullName ?? "Walk-in",
                        StaffId = p.ReceivedByUserId,
                        StaffName = FormatStaff(p.ReceivedByUser),
                        Type = ClassifyPayment(p, typeBySale)
                    };
                }).ToList()
            };

            return Result<SalesReportDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sales report for tenant {TenantId}", tenantId);
            return Result<SalesReportDto>.Failure("Failed to get sales report / فشل جلب تقرير المبيعات");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<RefundsReportDto>> GetRefundsReportAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate,
        string? method = null, Guid? staffId = null, string? buyer = null)
    {
        var bounds = ValidateRange(fromDate, toDate);
        if (!bounds.ok)
            return Result<RefundsReportDto>.Failure(bounds.error!);

        try
        {
            var (utcStart, utcEnd) = MembershipOperational.CairoInclusiveRangeUtc(fromDate, toDate);

            var refundsAll = await _dbContext.Refunds.AsNoTracking()
                .Include(r => r.Sale)
                    .ThenInclude(s => s!.Member)
                .Include(r => r.RequestedByUser)
                .Include(r => r.ApprovedByUser)
                .Where(r => r.TenantId == tenantId
                         && r.Status == "executed"
                         && r.ExecutedAt != null
                         && r.ExecutedAt >= utcStart
                         && r.ExecutedAt < utcEnd)
                .OrderByDescending(r => r.ExecutedAt)
                .ToListAsync();

            static Guid StaffIdOf(Refund r) => r.ApprovedByUserId ?? r.RequestedByUserId;
            static AppUser? StaffOf(Refund r) => r.ApprovedByUser ?? r.RequestedByUser;

            var staffOptions = refundsAll
                .GroupBy(StaffIdOf)
                .Select(g => new SalesReportStaffOptionDto
                {
                    UserId = g.Key,
                    Name = FormatStaff(StaffOf(g.First()))
                })
                .OrderBy(s => s.Name)
                .ToList();

            var methodOptions = refundsAll
                .Select(r => string.IsNullOrEmpty(r.Method) ? "unknown" : r.Method)
                .Distinct()
                .OrderBy(m => m)
                .ToList();

            IEnumerable<Refund> filtered = refundsAll;
            if (staffId.HasValue)
                filtered = filtered.Where(r => StaffIdOf(r) == staffId.Value);
            if (!string.IsNullOrWhiteSpace(method))
                filtered = filtered.Where(r => r.Method == method);
            if (buyer == "member")
                filtered = filtered.Where(r => r.Sale != null && r.Sale.MemberId != null);
            else if (buyer == "walkin")
                filtered = filtered.Where(r => r.Sale == null || r.Sale.MemberId == null);

            var refunds = filtered.ToList();

            var saleIds = refunds.Select(r => r.SaleId).Distinct().ToList();
            var invoices = saleIds.Count == 0
                ? new List<Invoice>()
                : await _dbContext.Invoices.AsNoTracking()
                    .Where(i => i.TenantId == tenantId
                             && i.SaleId != null
                             && saleIds.Contains(i.SaleId.Value)
                             && (i.Type == "invoice" || i.Type == "credit_note"))
                    .ToListAsync();

            Invoice? OriginalInvoiceFor(Refund r) =>
                invoices.FirstOrDefault(i => i.SaleId == r.SaleId && i.Type == "invoice");

            Invoice? CreditNoteFor(Refund r) =>
                invoices.FirstOrDefault(i => i.Id == r.CreditNoteInvoiceId || i.RefundId == r.Id)
                ?? invoices.FirstOrDefault(i => i.SaleId == r.SaleId && i.Type == "credit_note");

            var total = refunds.Sum(r => r.Amount);
            var count = refunds.Count;

            var dto = new RefundsReportDto
            {
                From = fromDate,
                To = toDate,
                Total = total,
                CashTotal = refunds.Where(r => r.Method == "cash").Sum(r => r.Amount),
                CreditTotal = refunds.Where(r => r.Method == "credit").Sum(r => r.Amount),
                GatewayTotal = refunds.Where(r => r.Method == "gateway").Sum(r => r.Amount),
                Count = count,
                SaleCount = saleIds.Count,
                Average = count == 0 ? 0m : Math.Round(total / count, 2, MidpointRounding.AwayFromZero),
                Truncated = count > ListCap,
                Staff = staffOptions,
                MethodOptions = methodOptions,
                Items = refunds.Take(ListCap).Select(r =>
                {
                    var orig = OriginalInvoiceFor(r);
                    var credit = CreditNoteFor(r);
                    var staff = StaffOf(r);
                    return new RefundsReportRowDto
                    {
                        Id = r.Id,
                        ExecutedAtUtc = r.ExecutedAt!.Value,
                        Amount = r.Amount,
                        Method = r.Method,
                        Reason = r.Reason ?? string.Empty,
                        SaleId = r.SaleId,
                        MemberId = r.Sale?.MemberId,
                        MemberName = r.Sale?.Member?.FullName ?? "Walk-in",
                        StaffId = StaffIdOf(r),
                        StaffName = FormatStaff(staff),
                        OriginalInvoiceId = orig?.Id,
                        OriginalInvoiceNumber = orig?.InvoiceNumber,
                        CreditNoteId = credit?.Id,
                        CreditNoteNumber = credit?.InvoiceNumber
                    };
                }).ToList()
            };

            return Result<RefundsReportDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting refunds report for tenant {TenantId}", tenantId);
            return Result<RefundsReportDto>.Failure("Failed to get refunds report / فشل جلب تقرير الاسترداد");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<MembershipsReportDto>> GetMembershipsReportAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate,
        Guid? planId = null, Guid? staffId = null, string? type = null)
    {
        var bounds = ValidateRange(fromDate, toDate);
        if (!bounds.ok)
            return Result<MembershipsReportDto>.Failure(bounds.error!);

        var kind = string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToLowerInvariant();
        if (kind is not "new" and not "renewal")
            kind = null;

        try
        {
            var (utcStart, utcEnd) = MembershipOperational.CairoInclusiveRangeUtc(fromDate, toDate);

            var started = await _dbContext.Memberships.AsNoTracking()
                .Include(m => m.Member)
                .Include(m => m.Plan)
                .Where(m => m.TenantId == tenantId && m.StartDate >= fromDate && m.StartDate <= toDate)
                .OrderByDescending(m => m.StartDate)
                .ThenByDescending(m => m.CreatedAtUtc)
                .ToListAsync();

            var membershipIds = started.Select(m => m.Id).ToList();

            var lines = membershipIds.Count == 0
                ? new List<SaleLine>()
                : await _dbContext.SaleLines.AsNoTracking()
                    .Where(l => l.TenantId == tenantId
                             && l.ReferenceId != null
                             && membershipIds.Contains(l.ReferenceId.Value)
                             && (l.LineType == "membership" || l.LineType == "trial" || l.LineType == "day_pass"))
                    .ToListAsync();

            var lineSaleIds = lines.Select(l => l.SaleId).Distinct().ToList();

            var payments = membershipIds.Count == 0
                ? new List<PaymentTransaction>()
                : await _dbContext.PaymentTransactions.AsNoTracking()
                    .Include(p => p.ReceivedByUser)
                    .Where(p => p.TenantId == tenantId
                             && p.Status == "success"
                             && p.Amount > 0
                             && ((p.MembershipId != null && membershipIds.Contains(p.MembershipId.Value))
                                 || (p.SaleId != null && lineSaleIds.Contains(p.SaleId.Value))))
                    .ToListAsync();

            var saleIds = lineSaleIds
                .Concat(payments.Where(p => p.SaleId.HasValue).Select(p => p.SaleId!.Value))
                .Distinct()
                .ToList();

            var sales = saleIds.Count == 0
                ? new List<Sale>()
                : await _dbContext.Sales.AsNoTracking()
                    .Include(s => s.SoldByUser)
                    .Where(s => s.TenantId == tenantId && saleIds.Contains(s.Id))
                    .ToListAsync();

            var saleLinesBySale = saleIds.Count == 0
                ? new Dictionary<Guid, List<SaleLine>>()
                : (await _dbContext.SaleLines.AsNoTracking()
                    .Where(l => l.TenantId == tenantId && saleIds.Contains(l.SaleId))
                    .ToListAsync())
                    .GroupBy(l => l.SaleId)
                    .ToDictionary(g => g.Key, g => g.ToList());

            var refunds = saleIds.Count == 0
                ? new List<Refund>()
                : await _dbContext.Refunds.AsNoTracking()
                    .Where(r => r.TenantId == tenantId
                             && r.Status == "executed"
                             && saleIds.Contains(r.SaleId))
                    .ToListAsync();

            var invoices = saleIds.Count == 0
                ? new List<Invoice>()
                : await _dbContext.Invoices.AsNoTracking()
                    .Where(i => i.TenantId == tenantId
                             && i.Type == "invoice"
                             && i.SaleId != null
                             && saleIds.Contains(i.SaleId.Value))
                    .ToListAsync();

            var salesById = sales.ToDictionary(s => s.Id);
            var invoiceBySale = invoices
                .GroupBy(i => i.SaleId!.Value)
                .ToDictionary(g => g.Key, g => g.First());
            var refundsBySale = refunds
                .GroupBy(r => r.SaleId)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
            var lineByMembership = lines
                .Where(l => l.ReferenceId.HasValue)
                .GroupBy(l => l.ReferenceId!.Value)
                .ToDictionary(g => g.Key, g => g.First());
            var paymentsByMembership = payments
                .Where(p => p.MembershipId.HasValue && membershipIds.Contains(p.MembershipId.Value))
                .GroupBy(p => p.MembershipId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var work = new List<MembershipsReportRowDto>(started.Count);
            foreach (var m in started)
            {
                Sale? sale = null;
                if (lineByMembership.TryGetValue(m.Id, out var line)
                    && salesById.TryGetValue(line.SaleId, out var fromLine))
                    sale = fromLine;
                else if (paymentsByMembership.TryGetValue(m.Id, out var memPays))
                {
                    var sid = memPays.FirstOrDefault(p => p.SaleId.HasValue)?.SaleId;
                    if (sid.HasValue && salesById.TryGetValue(sid.Value, out var fromPay))
                        sale = fromPay;
                }

                var rowPays = paymentsByMembership.TryGetValue(m.Id, out var listed)
                    ? listed
                    : new List<PaymentTransaction>();
                if (rowPays.Count == 0 && sale != null)
                    rowPays = payments.Where(p => p.SaleId == sale.Id).ToList();

                var cashIn = rowPays.Sum(p => p.Amount);
                var saleType = sale != null && saleLinesBySale.TryGetValue(sale.Id, out var sLines)
                    ? ClassifySaleLines(sLines)
                    : "unknown";
                var membershipSale = saleType == "membership";
                var refundedAmount = membershipSale && refundsBySale.TryGetValue(sale!.Id, out var ra) ? ra : 0m;
                var saleRefunded = membershipSale
                    && sale != null
                    && (sale.Status is "refunded" or "partially_refunded" || refundedAmount > 0m);
                var amount = cashIn - refundedAmount;
                var renewal = IsRenewal(m);
                var staffUser = sale?.SoldByUser
                    ?? rowPays.Select(p => p.ReceivedByUser).FirstOrDefault(u => u != null);
                var staffIdOf = sale?.SoldByUserId
                    ?? rowPays.Select(p => p.ReceivedByUserId).FirstOrDefault(id => id.HasValue);
                Invoice? inv = null;
                if (sale != null)
                    invoiceBySale.TryGetValue(sale.Id, out inv);

                var effective = MembershipOperational.GetEffectiveStatus(m, toDate);
                work.Add(new MembershipsReportRowDto
                {
                    Id = m.Id,
                    MemberId = m.MemberId,
                    MemberName = m.Member?.FullName ?? "—",
                    PlanId = m.PlanId,
                    PlanName = m.Plan?.Name ?? "—",
                    Type = renewal ? "renewal" : "new",
                    StaffId = staffIdOf,
                    StaffName = FormatStaff(staffUser),
                    InvoiceId = inv?.Id,
                    InvoiceNumber = inv?.InvoiceNumber,
                    Amount = amount,
                    Refunded = saleRefunded,
                    Status = saleRefunded ? "refunded" : effective,
                    StartDate = m.StartDate,
                    EndDate = m.EndDate
                });
            }

            var planOptions = started
                .GroupBy(m => m.PlanId)
                .Select(g => new MembershipsReportPlanOptionDto
                {
                    PlanId = g.Key,
                    Name = g.Select(x => x.Plan?.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "—"
                })
                .OrderBy(p => p.Name)
                .ToList();

            var staffOptions = work
                .GroupBy(r => r.StaffId)
                .Select(g => new SalesReportStaffOptionDto
                {
                    UserId = g.Key,
                    Name = g.Select(x => x.StaffName).FirstOrDefault(n => n != "—") ?? "Unassigned"
                })
                .OrderBy(s => s.Name)
                .ToList();

            IEnumerable<MembershipsReportRowDto> filtered = work;
            if (planId.HasValue)
                filtered = filtered.Where(r => r.PlanId == planId.Value);
            if (staffId.HasValue)
                filtered = filtered.Where(r => r.StaffId == staffId.Value);
            if (kind == "new")
                filtered = filtered.Where(r => r.Type == "new");
            else if (kind == "renewal")
                filtered = filtered.Where(r => r.Type == "renewal");

            var rows = filtered.ToList();

            var cancelled = await _dbContext.Memberships.AsNoTracking()
                .CountAsync(m => m.TenantId == tenantId
                              && m.Status == "cancelled"
                              && m.UpdatedAtUtc != null
                              && m.UpdatedAtUtc >= utcStart
                              && m.UpdatedAtUtc < utcEnd);

            var expired = await _dbContext.Memberships.AsNoTracking()
                .CountAsync(m => m.TenantId == tenantId
                              && m.EndDate >= fromDate
                              && m.EndDate <= toDate
                              && (m.Status == "expired" || m.Status == "cancelled"));

            var dto = new MembershipsReportDto
            {
                From = fromDate,
                To = toDate,
                Started = rows.Count,
                NewCount = rows.Count(r => r.Type == "new"),
                RenewalCount = rows.Count(r => r.Type == "renewal"),
                Revenue = rows.Sum(r => r.Amount),
                RefundedCount = rows.Count(r => r.Refunded),
                Cancelled = cancelled,
                Expired = expired,
                Truncated = rows.Count > ListCap,
                Staff = staffOptions,
                Plans = planOptions,
                ByPlan = rows
                    .GroupBy(r => r.PlanId)
                    .Select(g => new MembershipsReportPlanBreakdownDto
                    {
                        PlanId = g.Key,
                        PlanName = g.Select(x => x.PlanName).FirstOrDefault() ?? "—",
                        NewCount = g.Count(x => x.Type == "new"),
                        RenewalCount = g.Count(x => x.Type == "renewal"),
                        Revenue = g.Sum(x => x.Amount)
                    })
                    .OrderByDescending(p => p.NewCount + p.RenewalCount)
                    .ToList(),
                StartedRows = rows.Take(ListCap).ToList()
            };

            return Result<MembershipsReportDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting memberships report for tenant {TenantId}", tenantId);
            return Result<MembershipsReportDto>.Failure("Failed to get memberships report / فشل جلب تقرير الاشتراكات");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<ProductsReportDto>> GetProductsReportAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate,
        Guid? productId = null, Guid? staffId = null, string? method = null)
    {
        var bounds = ValidateRange(fromDate, toDate);
        if (!bounds.ok)
            return Result<ProductsReportDto>.Failure(bounds.error!);

        var methodKey = string.IsNullOrWhiteSpace(method) ? null : method.Trim().ToLowerInvariant();

        try
        {
            var (utcStart, utcEnd) = MembershipOperational.CairoInclusiveRangeUtc(fromDate, toDate);

            var lines = await _dbContext.SaleLines.AsNoTracking()
                .Where(l => l.TenantId == tenantId
                         && l.LineType == "retail"
                         && l.Sale != null
                         && l.Sale.CreatedAtUtc >= utcStart
                         && l.Sale.CreatedAtUtc < utcEnd
                         && l.Sale.Status != "refunded")
                .ToListAsync();

            var saleIds = lines.Select(l => l.SaleId).Distinct().ToList();
            var sales = saleIds.Count == 0
                ? new List<Sale>()
                : await _dbContext.Sales.AsNoTracking()
                    .Include(s => s.SoldByUser)
                    .Where(s => s.TenantId == tenantId && saleIds.Contains(s.Id))
                    .ToListAsync();
            var salesById = sales.ToDictionary(s => s.Id);

            var payments = saleIds.Count == 0
                ? new List<PaymentTransaction>()
                : await _dbContext.PaymentTransactions.AsNoTracking()
                    .Where(p => p.TenantId == tenantId
                             && p.Status == "success"
                             && p.Amount > 0
                             && p.SaleId != null
                             && saleIds.Contains(p.SaleId.Value))
                    .ToListAsync();

            var invoices = saleIds.Count == 0
                ? new List<Invoice>()
                : await _dbContext.Invoices.AsNoTracking()
                    .Where(i => i.TenantId == tenantId
                             && i.Type == "invoice"
                             && i.SaleId != null
                             && saleIds.Contains(i.SaleId.Value))
                    .ToListAsync();

            var productIds = lines.Where(l => l.ReferenceId.HasValue).Select(l => l.ReferenceId!.Value).Distinct().ToList();
            var products = productIds.Count == 0
                ? new Dictionary<Guid, Product>()
                : await _dbContext.Products.AsNoTracking()
                    .Where(p => p.TenantId == tenantId && productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id);

            var invoiceBySale = invoices
                .GroupBy(i => i.SaleId!.Value)
                .ToDictionary(g => g.Key, g => g.First());
            var paysBySale = payments
                .Where(p => p.SaleId.HasValue)
                .GroupBy(p => p.SaleId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            string PaymentLabel(Guid saleId)
            {
                if (!paysBySale.TryGetValue(saleId, out var pays) || pays.Count == 0)
                    return "—";
                var methods = pays
                    .Select(p => (p.Method ?? string.Empty).Trim().ToLowerInvariant())
                    .Where(m => m.Length > 0)
                    .Distinct()
                    .ToList();
                if (methods.Count == 0) return "—";
                if (methods.Count == 1) return methods[0];
                return "mixed";
            }

            bool SaleMatchesMethod(Guid saleId)
            {
                if (methodKey == null) return true;
                return paysBySale.TryGetValue(saleId, out var pays)
                    && pays.Any(p => (p.Method ?? string.Empty).Trim().ToLowerInvariant() == methodKey);
            }

            var productOptions = lines
                .Where(l => l.ReferenceId.HasValue)
                .GroupBy(l => l.ReferenceId!.Value)
                .Select(g =>
                {
                    products.TryGetValue(g.Key, out var p);
                    return new ProductsReportProductOptionDto
                    {
                        ProductId = g.Key,
                        Name = !string.IsNullOrWhiteSpace(p?.Name)
                            ? p!.Name
                            : (g.Select(x => x.Description).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "—")
                    };
                })
                .OrderBy(p => p.Name)
                .ToList();

            var staffOptions = sales
                .GroupBy(s => s.SoldByUserId)
                .Select(g => new SalesReportStaffOptionDto
                {
                    UserId = g.Key,
                    Name = FormatStaff(g.Select(s => s.SoldByUser).FirstOrDefault())
                })
                .OrderBy(s => s.Name)
                .ToList();

            var methodOptions = payments
                .Select(p => (p.Method ?? string.Empty).Trim().ToLowerInvariant())
                .Where(m => m.Length > 0)
                .Distinct()
                .OrderBy(m => m)
                .ToList();

            IEnumerable<SaleLine> filtered = lines;
            if (productId.HasValue)
                filtered = filtered.Where(l => l.ReferenceId == productId.Value);
            if (staffId.HasValue)
                filtered = filtered.Where(l => salesById.TryGetValue(l.SaleId, out var s) && s.SoldByUserId == staffId.Value);
            if (methodKey != null)
                filtered = filtered.Where(l => SaleMatchesMethod(l.SaleId));

            var filteredList = filtered
                .OrderByDescending(l => salesById.TryGetValue(l.SaleId, out var s) ? s.CreatedAtUtc : l.CreatedAtUtc)
                .ToList();

            string ProductNameOf(SaleLine l)
            {
                if (l.ReferenceId.HasValue && products.TryGetValue(l.ReferenceId.Value, out var p) && !string.IsNullOrWhiteSpace(p.Name))
                    return p.Name;
                return string.IsNullOrWhiteSpace(l.Description) ? "—" : l.Description;
            }

            var ranked = filteredList
                .GroupBy(l => l.ReferenceId)
                .Select(g => new ProductsReportRankedDto
                {
                    ProductId = g.Key,
                    Name = ProductNameOf(g.First()),
                    UnitsSold = g.Sum(x => x.Qty),
                    Revenue = g.Sum(x => x.LineTotal)
                })
                .OrderByDescending(r => r.UnitsSold)
                .ThenByDescending(r => r.Revenue)
                .ToList();

            var top = ranked.FirstOrDefault();
            var dto = new ProductsReportDto
            {
                From = fromDate,
                To = toDate,
                Revenue = filteredList.Sum(l => l.LineTotal),
                UnitsSold = filteredList.Sum(l => l.Qty),
                TransactionCount = filteredList.Select(l => l.SaleId).Distinct().Count(),
                TopProductId = top?.ProductId,
                TopProductName = top?.Name,
                Truncated = filteredList.Count > ListCap,
                Staff = staffOptions,
                Products = productOptions,
                MethodOptions = methodOptions,
                TopProducts = ranked.Take(50).ToList(),
                Lines = filteredList.Take(ListCap).Select(l =>
                {
                    salesById.TryGetValue(l.SaleId, out var sale);
                    Invoice? inv = null;
                    if (sale != null)
                        invoiceBySale.TryGetValue(sale.Id, out inv);
                    return new ProductsReportLineDto
                    {
                        SaleLineId = l.Id,
                        SoldAtUtc = sale?.CreatedAtUtc ?? l.CreatedAtUtc,
                        SaleId = l.SaleId,
                        InvoiceId = inv?.Id,
                        InvoiceNumber = inv?.InvoiceNumber,
                        ProductId = l.ReferenceId,
                        ProductName = ProductNameOf(l),
                        Quantity = l.Qty,
                        StaffId = sale?.SoldByUserId,
                        StaffName = FormatStaff(sale?.SoldByUser),
                        Payment = PaymentLabel(l.SaleId),
                        Revenue = l.LineTotal
                    };
                }).ToList()
            };

            return Result<ProductsReportDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting products report for tenant {TenantId}", tenantId);
            return Result<ProductsReportDto>.Failure("Failed to get products report / فشل جلب تقرير المنتجات");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<StaffShiftsReportDto>> GetStaffShiftsReportAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate,
        Guid? staffId = null, Guid? shiftId = null)
    {
        var bounds = ValidateRange(fromDate, toDate);
        if (!bounds.ok)
            return Result<StaffShiftsReportDto>.Failure(bounds.error!);

        try
        {
            var (utcStart, utcEnd) = MembershipOperational.CairoInclusiveRangeUtc(fromDate, toDate);

            var paymentsAll = await _dbContext.PaymentTransactions.AsNoTracking()
                .Include(p => p.ReceivedByUser)
                .Where(p => p.TenantId == tenantId
                         && p.Status == "success"
                         && p.Amount > 0
                         && p.PaidAtUtc >= utcStart
                         && p.PaidAtUtc < utcEnd)
                .ToListAsync();

            var refundsAll = await _dbContext.Refunds.AsNoTracking()
                .Include(r => r.RequestedByUser)
                .Include(r => r.ApprovedByUser)
                .Where(r => r.TenantId == tenantId
                         && r.Status == "executed"
                         && r.ExecutedAt != null
                         && r.ExecutedAt >= utcStart
                         && r.ExecutedAt < utcEnd)
                .ToListAsync();

            var shiftsAll = await _dbContext.Shifts.AsNoTracking()
                .Include(s => s.User)
                .Where(s => s.TenantId == tenantId && s.OpenedAt >= utcStart && s.OpenedAt < utcEnd)
                .OrderBy(s => s.OpenedAt)
                .ToListAsync();

            static Guid StaffIdOfRefund(Refund r) => r.ApprovedByUserId ?? r.RequestedByUserId;
            static AppUser? StaffOfRefund(Refund r) => r.ApprovedByUser ?? r.RequestedByUser;

            var refundIds = refundsAll.Select(r => r.Id).ToList();
            var refundMoves = refundIds.Count == 0
                ? new List<CashMovement>()
                : await _dbContext.CashMovements.AsNoTracking()
                    .Where(m => m.TenantId == tenantId
                             && m.Type == "refund"
                             && m.ReferenceId != null
                             && refundIds.Contains(m.ReferenceId.Value))
                    .ToListAsync();
            var shiftIdByRefund = refundMoves
                .Where(m => m.ReferenceId.HasValue)
                .GroupBy(m => m.ReferenceId!.Value)
                .ToDictionary(g => g.Key, g => g.First().ShiftId);

            var staffOptions = paymentsAll
                .Select(p => p.ReceivedByUserId)
                .Concat(refundsAll.Select(r => (Guid?)StaffIdOfRefund(r)))
                .Concat(shiftsAll.Select(s => (Guid?)s.UserId))
                .Distinct()
                .Select(id =>
                {
                    var name = FormatStaff(
                        paymentsAll.FirstOrDefault(p => p.ReceivedByUserId == id)?.ReceivedByUser
                        ?? refundsAll.Select(StaffOfRefund).FirstOrDefault(u => u != null && u.Id == id)
                        ?? shiftsAll.FirstOrDefault(s => s.UserId == id)?.User);
                    return new SalesReportStaffOptionDto
                    {
                        UserId = id,
                        Name = name == "—" ? "Unassigned" : name
                    };
                })
                .OrderBy(s => s.Name)
                .ToList();

            var shiftOptions = shiftsAll
                .Select(s => new StaffShiftOptionDto
                {
                    ShiftId = s.Id,
                    Name = ShiftLabel(s)
                })
                .ToList();

            IEnumerable<PaymentTransaction> pays = paymentsAll;
            IEnumerable<Refund> refunds = refundsAll;
            IEnumerable<Shift> shifts = shiftsAll;
            if (staffId.HasValue)
            {
                pays = pays.Where(p => p.ReceivedByUserId == staffId.Value);
                refunds = refunds.Where(r => StaffIdOfRefund(r) == staffId.Value);
                shifts = shifts.Where(s => s.UserId == staffId.Value);
            }
            if (shiftId.HasValue)
            {
                pays = pays.Where(p => p.ShiftId == shiftId.Value);
                refunds = refunds.Where(r =>
                    shiftIdByRefund.TryGetValue(r.Id, out var sid) && sid == shiftId.Value);
                shifts = shifts.Where(s => s.Id == shiftId.Value);
            }

            var payList = pays.ToList();
            var refundList = refunds.ToList();
            var shiftList = shifts.ToList();

            var staffIds = payList.Select(p => p.ReceivedByUserId)
                .Concat(refundList.Select(r => (Guid?)StaffIdOfRefund(r)))
                .Concat(shiftList.Select(s => (Guid?)s.UserId))
                .Distinct()
                .ToList();

            var staffRows = staffIds
                .Select(id =>
                {
                    var name = FormatStaff(
                        payList.FirstOrDefault(p => p.ReceivedByUserId == id)?.ReceivedByUser
                        ?? refundList.Select(StaffOfRefund).FirstOrDefault(u => u != null && u.Id == id)
                        ?? shiftList.FirstOrDefault(s => s.UserId == id)?.User);
                    return new StaffCashInRowDto
                    {
                        UserId = id,
                        StaffName = name == "—" ? "Unassigned" : name,
                        PaymentCount = payList.Count(p => p.ReceivedByUserId == id),
                        CashIn = payList.Where(p => p.ReceivedByUserId == id).Sum(p => p.Amount),
                        Refunds = refundList.Where(r => StaffIdOfRefund(r) == id).Sum(r => r.Amount),
                        ShiftCount = shiftList.Count(s => s.UserId == id)
                    };
                })
                .OrderByDescending(s => s.CashIn)
                .ToList();

            var saleIds = payList.Where(p => p.SaleId.HasValue).Select(p => p.SaleId!.Value)
                .Concat(refundList.Select(r => r.SaleId))
                .Distinct()
                .ToList();
            var invoices = saleIds.Count == 0
                ? new List<Invoice>()
                : await _dbContext.Invoices.AsNoTracking()
                    .Where(i => i.TenantId == tenantId
                             && i.Type == "invoice"
                             && i.SaleId != null
                             && saleIds.Contains(i.SaleId.Value))
                    .ToListAsync();
            var invoiceBySale = invoices
                .GroupBy(i => i.SaleId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var drill = new List<StaffReportTxDto>();
            if (staffId.HasValue || shiftId.HasValue)
            {
                drill.AddRange(payList.Select(p =>
                {
                    Invoice? inv = null;
                    if (p.SaleId.HasValue)
                        invoiceBySale.TryGetValue(p.SaleId.Value, out inv);
                    return new StaffReportTxDto
                    {
                        Type = "sale",
                        AtUtc = p.PaidAtUtc,
                        Amount = p.Amount,
                        Method = p.Method ?? string.Empty,
                        InvoiceId = inv?.Id,
                        InvoiceNumber = inv?.InvoiceNumber,
                        StaffId = p.ReceivedByUserId,
                        StaffName = FormatStaff(p.ReceivedByUser)
                    };
                }));
                drill.AddRange(refundList.Select(r =>
                {
                    Invoice? inv = null;
                    invoiceBySale.TryGetValue(r.SaleId, out inv);
                    var staff = StaffOfRefund(r);
                    return new StaffReportTxDto
                    {
                        Type = "refund",
                        AtUtc = r.ExecutedAt!.Value,
                        Amount = r.Amount,
                        Method = r.Method ?? string.Empty,
                        InvoiceId = inv?.Id,
                        InvoiceNumber = inv?.InvoiceNumber,
                        StaffId = StaffIdOfRefund(r),
                        StaffName = FormatStaff(staff)
                    };
                }));
                drill = drill.OrderByDescending(t => t.AtUtc).Take(ListCap).ToList();
            }

            var dto = new StaffShiftsReportDto
            {
                From = fromDate,
                To = toDate,
                Sales = payList.Sum(p => p.Amount),
                TransactionCount = payList.Count,
                Refunds = refundList.Sum(r => r.Amount),
                ShiftCount = shiftList.Count,
                Truncated = (staffId.HasValue || shiftId.HasValue)
                    && (payList.Count + refundList.Count) > ListCap,
                StaffOptions = staffOptions,
                ShiftOptions = shiftOptions,
                StaffCashIn = staffRows,
                Shifts = shiftList.Select(s => new StaffShiftRowDto
                {
                    ShiftId = s.Id,
                    UserId = s.UserId,
                    StaffName = FormatStaff(s.User),
                    OpenedAt = s.OpenedAt,
                    ClosedAt = s.ClosedAt,
                    Status = s.Status,
                    Sales = payList.Where(p => p.ShiftId == s.Id).Sum(p => p.Amount),
                    Refunds = refundList
                        .Where(r => shiftIdByRefund.TryGetValue(r.Id, out var sid) && sid == s.Id)
                        .Sum(r => r.Amount),
                    OpeningFloat = s.OpeningFloat,
                    ExpectedCash = s.ExpectedCash,
                    CountedCash = s.CountedCash,
                    Variance = s.Variance
                }).ToList(),
                Transactions = drill
            };

            return Result<StaffShiftsReportDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting staff/shifts report for tenant {TenantId}", tenantId);
            return Result<StaffShiftsReportDto>.Failure("Failed to get staff report / فشل جلب تقرير الموظفين");
        }
    }

    private static string ShiftLabel(Shift s)
    {
        var staff = FormatStaff(s.User);
        var opened = MembershipOperational.ToCairoDate(s.OpenedAt);
        return $"{(staff == "—" ? "Shift" : staff)} · {opened:dd MMM}";
    }

    private static (bool ok, string? error) ValidateRange(DateOnly fromDate, DateOnly toDate)
    {
        if (fromDate > toDate)
            return (false, "From date must be before To date / تاريخ البداية يجب أن يسبق النهاية");
        if (toDate.DayNumber - fromDate.DayNumber > MaxRangeDays)
            return (false, "Date range cannot exceed 90 days / لا يمكن أن يتجاوز النطاق 90 يوماً");
        return (true, null);
    }

    private static string ClassifyPayment(
        PaymentTransaction p, IReadOnlyDictionary<Guid, string> typeBySale)
    {
        if (!p.SaleId.HasValue) return "unknown";
        return typeBySale.TryGetValue(p.SaleId.Value, out var t) ? t : "unknown";
    }

    private static string ClassifySaleLines(IEnumerable<SaleLine> lines)
    {
        var types = lines
            .Select(l => (l.LineType ?? string.Empty).Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();
        if (types.Count == 0) return "unknown";
        var membership = types.Any(IsMembershipLineType);
        var product = types.Contains("retail");
        if (membership && product) return "mixed";
        if (membership) return "membership";
        if (product) return "product";
        return "other";
    }

    private static bool IsRenewal(Membership m) =>
        m.LastRenewalDate != null || !string.IsNullOrWhiteSpace(m.PlanTransitionMode);

    private static bool IsMembershipLineType(string lineType) =>
        lineType is "membership" or "trial" or "day_pass";

    private static string FormatStaff(AppUser? user)
    {
        if (user == null) return "—";
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrEmpty(name) ? "—" : name;
    }
}
