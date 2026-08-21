namespace GMS.Application.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.ZReports;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Models;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Daily closing Z-Report: aggregates payment methods, sales by line type, discounts, shift
/// reconciliation, and outstanding balances for a tenant's Cairo (Egypt Standard Time) business
/// day. Immutable once built for a given (TenantId, ReportDate) — a second BuildAsync call returns
/// the existing snapshot untouched; only RegenerateAsync (manager+, audited) recomputes it.
/// </summary>
public class ZReportService : IZReportService
{
    private static readonly TimeZoneInfo CairoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private readonly GymFlowProDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly IZReportPdfRenderer _pdfRenderer;
    private readonly ILogger<ZReportService> _logger;

    public ZReportService(
        GymFlowProDbContext dbContext, IAuditService auditService, IZReportPdfRenderer pdfRenderer, ILogger<ZReportService> logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _pdfRenderer = pdfRenderer;
        _logger = logger;
    }

    public async Task<Result<ZReportDto>> BuildAsync(Guid tenantId, DateOnly reportDate, Guid? requestedByUserId = null)
    {
        try
        {
            var existing = await _dbContext.ZReports
                .FirstOrDefaultAsync(z => z.TenantId == tenantId && z.ReportDate == reportDate);

            if (existing != null)
                return Result<ZReportDto>.Success(MapToDto(existing));

            var payload = await ComputeAggregationAsync(tenantId, reportDate);
            var zReport = new ZReport
            {
                TenantId = tenantId,
                ReportDate = reportDate,
                PayloadJson = JsonSerializer.Serialize(payload),
                GeneratedAt = DateTime.UtcNow,
                GeneratedByUserId = requestedByUserId
            };

            _dbContext.ZReports.Add(zReport);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Z-Report built: tenant {TenantId} date {ReportDate}", tenantId, reportDate);

            return Result<ZReportDto>.Success(MapToDto(zReport, payload));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building Z-Report for tenant {TenantId} date {ReportDate}", tenantId, reportDate);
            return Result<ZReportDto>.Failure("Failed to build Z-Report / فشل إنشاء تقرير الإقفال", ex.Message);
        }
    }

    public async Task<Result<ZReportDto>> GetAsync(Guid tenantId, DateOnly reportDate)
    {
        try
        {
            var existing = await _dbContext.ZReports
                .FirstOrDefaultAsync(z => z.TenantId == tenantId && z.ReportDate == reportDate);

            if (existing == null)
                return Fail(ZReportFailureReasons.NotFound, "Z-Report not found for this date / تقرير الإقفال غير موجود لهذا التاريخ");

            return Result<ZReportDto>.Success(MapToDto(existing));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Z-Report for tenant {TenantId} date {ReportDate}", tenantId, reportDate);
            return Result<ZReportDto>.Failure("Failed to retrieve Z-Report / فشل جلب تقرير الإقفال", ex.Message);
        }
    }

    public async Task<Result<ZReportDto>> RegenerateAsync(Guid tenantId, DateOnly reportDate, Guid managerUserId)
    {
        try
        {
            var existing = await _dbContext.ZReports
                .FirstOrDefaultAsync(z => z.TenantId == tenantId && z.ReportDate == reportDate);

            if (existing == null)
                return Fail(ZReportFailureReasons.NotFound, "Z-Report not found for this date / تقرير الإقفال غير موجود لهذا التاريخ");

            var payload = await ComputeAggregationAsync(tenantId, reportDate);

            existing.PayloadJson = JsonSerializer.Serialize(payload);
            existing.PdfUrl = null; // stale — needs re-render on next PDF request
            existing.GeneratedAt = DateTime.UtcNow;
            existing.GeneratedByUserId = managerUserId;
            existing.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync("zreport.regenerate", "ZReport", existing.Id, null,
                new { reportDate, regeneratedByUserId = managerUserId });

            _logger.LogInformation("Z-Report regenerated: tenant {TenantId} date {ReportDate} by {ManagerId}", tenantId, reportDate, managerUserId);

            return Result<ZReportDto>.Success(MapToDto(existing, payload));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error regenerating Z-Report for tenant {TenantId} date {ReportDate}", tenantId, reportDate);
            return Result<ZReportDto>.Failure("Failed to regenerate Z-Report / فشل إعادة إنشاء تقرير الإقفال", ex.Message);
        }
    }

    public async Task<Result<byte[]>> GetPdfBytesAsync(Guid tenantId, DateOnly reportDate)
    {
        try
        {
            var existing = await _dbContext.ZReports
                .FirstOrDefaultAsync(z => z.TenantId == tenantId && z.ReportDate == reportDate);

            if (existing == null)
                return Result<byte[]>.Failure($"{ZReportFailureReasons.NotFound}|Z-Report not found for this date / تقرير الإقفال غير موجود لهذا التاريخ");

            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            var dto = MapToDto(existing);
            var pdfModel = BuildPdfModel(dto, tenant);

            return Result<byte[]>.Success(_pdfRenderer.Render(pdfModel));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering Z-Report PDF for tenant {TenantId} date {ReportDate}", tenantId, reportDate);
            return Result<byte[]>.Failure("Failed to render Z-Report PDF / فشل إنشاء ملف تقرير الإقفال", ex.Message);
        }
    }

    public async Task<Result<ShiftZReportListDto>> ListShiftClosingsAsync(
        Guid tenantId, DateOnly fromDate, DateOnly toDate)
    {
        if (fromDate > toDate)
            return Result<ShiftZReportListDto>.Failure("From date must be before To date / تاريخ البداية يجب أن يسبق النهاية");
        if (toDate.DayNumber - fromDate.DayNumber > 90)
            return Result<ShiftZReportListDto>.Failure("Date range cannot exceed 90 days / لا يمكن أن يتجاوز النطاق 90 يوماً");

        try
        {
            var (utcStart, utcEnd) = MembershipOperational.CairoInclusiveRangeUtc(fromDate, toDate);
            var shifts = await _dbContext.Shifts.AsNoTracking()
                .Include(s => s.User)
                .Where(s => s.TenantId == tenantId && s.OpenedAt >= utcStart && s.OpenedAt < utcEnd)
                .OrderByDescending(s => s.OpenedAt)
                .ToListAsync();

            var shiftIds = shifts.Select(s => s.Id).ToList();
            var payRows = shiftIds.Count == 0
                ? new List<(Guid ShiftId, decimal Amount)>()
                : (await _dbContext.PaymentTransactions.AsNoTracking()
                    .Where(p => p.TenantId == tenantId
                             && p.ShiftId != null
                             && shiftIds.Contains(p.ShiftId.Value)
                             && p.Status == "success"
                             && p.Amount > 0)
                    .Select(p => new { p.ShiftId, p.Amount })
                    .ToListAsync())
                    .Select(p => (ShiftId: p.ShiftId!.Value, Amount: p.Amount))
                    .ToList();
            var salesByShift = payRows
                .GroupBy(p => p.ShiftId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

            var refundMoves = shiftIds.Count == 0
                ? new List<CashMovement>()
                : await _dbContext.CashMovements.AsNoTracking()
                    .Where(m => m.TenantId == tenantId
                             && shiftIds.Contains(m.ShiftId)
                             && m.Type == "refund")
                    .ToListAsync();
            var refundsByShift = refundMoves
                .GroupBy(m => m.ShiftId)
                .ToDictionary(g => g.Key, g => g.Sum(m => Math.Abs(m.Amount)));

            var items = shifts.Select(s =>
            {
                var reveal = s.Status != "open";
                return new ShiftZReportListItemDto
                {
                    ShiftId = s.Id,
                    UserId = s.UserId,
                    StaffName = FormatStaff(s.User),
                    OpenedAt = s.OpenedAt,
                    ClosedAt = s.ClosedAt,
                    Status = s.Status,
                    Sales = salesByShift.GetValueOrDefault(s.Id),
                    Refunds = refundsByShift.GetValueOrDefault(s.Id),
                    ExpectedCash = reveal ? s.ExpectedCash : null,
                    CountedCash = reveal ? s.CountedCash : null,
                    Difference = reveal ? s.Variance : null
                };
            }).ToList();

            return Result<ShiftZReportListDto>.Success(new ShiftZReportListDto
            {
                From = fromDate,
                To = toDate,
                Items = items
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing shift Z-Reports for tenant {TenantId}", tenantId);
            return Result<ShiftZReportListDto>.Failure("Failed to list Z-Reports / فشل جلب تقارير الإقفال", ex.Message);
        }
    }

    public async Task<Result<ShiftZReportDto>> GetShiftClosingAsync(Guid tenantId, Guid shiftId)
    {
        try
        {
            var shift = await _dbContext.Shifts.AsNoTracking()
                .Include(s => s.User)
                .Include(s => s.Movements)
                .FirstOrDefaultAsync(s => s.Id == shiftId && s.TenantId == tenantId);

            if (shift == null)
                return Result<ShiftZReportDto>.Failure($"{ZReportFailureReasons.NotFound}|Shift not found / الوردية غير موجودة");

            var tenant = await _dbContext.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            var payments = await _dbContext.PaymentTransactions.AsNoTracking()
                .Where(p => p.TenantId == tenantId
                         && p.ShiftId == shiftId
                         && p.Status == "success"
                         && p.Amount > 0)
                .ToListAsync();

            var originSales = await _dbContext.Sales.AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.ShiftId == shiftId)
                .ToListAsync();
            if (originSales.Count == 0)
            {
                var paySaleIds = payments.Where(p => p.SaleId.HasValue).Select(p => p.SaleId!.Value).Distinct().ToList();
                if (paySaleIds.Count > 0)
                {
                    originSales = await _dbContext.Sales.AsNoTracking()
                        .Where(s => s.TenantId == tenantId && paySaleIds.Contains(s.Id))
                        .ToListAsync();
                }
            }

            var originSaleIds = originSales.Select(s => s.Id).ToList();
            var lines = originSaleIds.Count == 0
                ? new List<SaleLine>()
                : await _dbContext.SaleLines.AsNoTracking()
                    .Where(l => l.TenantId == tenantId && originSaleIds.Contains(l.SaleId))
                    .ToListAsync();

            var membershipIds = payments.Where(p => p.MembershipId.HasValue).Select(p => p.MembershipId!.Value)
                .Concat(lines.Where(l => l.LineType == "membership" && l.ReferenceId.HasValue).Select(l => l.ReferenceId!.Value))
                .Distinct()
                .ToList();
            var memberships = membershipIds.Count == 0
                ? new List<Membership>()
                : await _dbContext.Memberships.AsNoTracking()
                    .Where(m => m.TenantId == tenantId && membershipIds.Contains(m.Id))
                    .ToListAsync();
            var membershipById = memberships.ToDictionary(m => m.Id);
            var renewalMembershipIds = memberships.Where(IsRenewal).Select(m => m.Id).ToHashSet();

            var saleMembership = payments
                .Where(p => p.SaleId.HasValue && p.MembershipId.HasValue)
                .GroupBy(p => p.SaleId!.Value)
                .ToDictionary(g => g.Key, g => g.First().MembershipId!.Value);

            decimal membershipsRev = 0, renewalsRev = 0, productsRev = 0, otherRev = 0;
            int membershipsN = 0, renewalsN = 0, productsN = 0, otherN = 0;
            foreach (var line in lines)
            {
                var type = (line.LineType ?? "").Trim().ToLowerInvariant();
                if (type == "retail")
                {
                    productsRev += line.LineTotal;
                    productsN += 1;
                }
                else if (type == "membership")
                {
                    Guid? mid = null;
                    if (saleMembership.TryGetValue(line.SaleId, out var fromPay))
                        mid = fromPay;
                    else if (line.ReferenceId.HasValue && membershipById.ContainsKey(line.ReferenceId.Value))
                        mid = line.ReferenceId;
                    var renewal = mid.HasValue && renewalMembershipIds.Contains(mid.Value);
                    if (renewal)
                    {
                        renewalsRev += line.LineTotal;
                        renewalsN += 1;
                    }
                    else
                    {
                        membershipsRev += line.LineTotal;
                        membershipsN += 1;
                    }
                }
                else
                {
                    otherRev += line.LineTotal;
                    otherN += 1;
                }
            }

            var refundMoves = shift.Movements.Where(m => m.Type == "refund").ToList();
            var refundIds = refundMoves.Where(m => m.ReferenceId.HasValue).Select(m => m.ReferenceId!.Value).Distinct().ToList();
            var refundRows = refundIds.Count == 0
                ? new List<Refund>()
                : await _dbContext.Refunds.AsNoTracking()
                    .Where(r => r.TenantId == tenantId && refundIds.Contains(r.Id) && r.Status == "executed")
                    .ToListAsync();
            var refundById = refundRows.ToDictionary(r => r.Id);
            var refundsTotal = refundMoves.Sum(m =>
                m.ReferenceId.HasValue && refundById.TryGetValue(m.ReferenceId.Value, out var r)
                    ? r.Amount
                    : Math.Abs(m.Amount));

            var gross = originSales.Sum(s => s.Subtotal);
            var discounts = originSales.Sum(s => s.DiscountAmount + (s.ManualDiscountAmount ?? 0m));
            var methods = payments
                .Where(p => !string.IsNullOrEmpty(p.Method))
                .GroupBy(p => p.Method!)
                .Select(g => new ZReportMethodTotalDto { Method = g.Key, Count = g.Count(), Total = g.Sum(p => p.Amount) })
                .OrderBy(m => m.Method)
                .ToList();

            var cashSales = shift.Movements.Where(m => m.Type == "sale").Sum(m => m.Amount);
            var cashRefunds = refundMoves.Sum(m => Math.Abs(m.Amount));
            var cashExpenses = shift.Movements.Where(m => m.Type == "paid_out").Sum(m => Math.Abs(m.Amount));
            var cashPaidIn = shift.Movements.Where(m => m.Type == "paid_in").Sum(m => m.Amount);
            var floatAdjust = shift.Movements.Where(m => m.Type == "float_adjust").Sum(m => m.Amount);

            var isOpen = shift.Status == "open";
            var isFinal = shift.Status is "closed" or "approved";

            var dto = new ShiftZReportDto
            {
                ShiftId = shift.Id,
                TenantId = tenantId,
                UserId = shift.UserId,
                StaffName = FormatStaff(shift.User),
                GymName = tenant?.Name ?? string.Empty,
                OpenedAt = shift.OpenedAt,
                ClosedAt = shift.ClosedAt,
                Status = shift.Status,
                IsFinal = isFinal,
                RevealCash = !isOpen,
                GrossSales = gross,
                Discounts = discounts,
                Refunds = refundsTotal,
                NetSales = gross - discounts - refundsTotal,
                TransactionCount = payments.Count,
                Methods = methods,
                OpeningCash = shift.OpeningFloat,
                CashSales = cashSales,
                CashRefunds = cashRefunds,
                CashExpenses = cashExpenses,
                CashPaidIn = cashPaidIn,
                FloatAdjust = floatAdjust,
                ExpectedCash = isOpen ? null : shift.ExpectedCash,
                CountedCash = isOpen ? null : shift.CountedCash,
                Difference = isOpen ? null : shift.Variance,
                Memberships = membershipsRev,
                MembershipCount = membershipsN,
                Renewals = renewalsRev,
                RenewalCount = renewalsN,
                Products = productsRev,
                ProductCount = productsN,
                Other = otherRev,
                OtherCount = otherN,
                RefundCount = refundMoves.Count,
                DiscountCount = originSales.Count(s => s.DiscountAmount + (s.ManualDiscountAmount ?? 0m) != 0m)
            };

            return Result<ShiftZReportDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building shift closing Z-Report {ShiftId} for tenant {TenantId}", shiftId, tenantId);
            return Result<ShiftZReportDto>.Failure("Failed to get shift Z-Report / فشل جلب تقرير إقفال الوردية", ex.Message);
        }
    }

    public async Task<Result<byte[]>> GetShiftClosingPdfAsync(Guid tenantId, Guid shiftId)
    {
        var result = await GetShiftClosingAsync(tenantId, shiftId);
        if (!result.IsSuccess)
            return Result<byte[]>.Failure(result.Error ?? "Failed to get shift Z-Report / فشل جلب تقرير إقفال الوردية");

        try
        {
            var tenant = await _dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
            var pdf = _pdfRenderer.RenderShiftClosing(BuildShiftPdfModel(result.Data!, tenant));
            return Result<byte[]>.Success(pdf);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering shift closing PDF {ShiftId}", shiftId);
            return Result<byte[]>.Failure("Failed to render Z-Report PDF / فشل إنشاء ملف تقرير الإقفال", ex.Message);
        }
    }

    private static bool IsRenewal(Membership m) =>
        m.LastRenewalDate != null || !string.IsNullOrWhiteSpace(m.PlanTransitionMode);

    private static string FormatStaff(AppUser? user)
    {
        if (user == null) return "—";
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? "—" : name;
    }

    private static ShiftZReportPdfModel BuildShiftPdfModel(ShiftZReportDto dto, Tenant? tenant) => new()
    {
        GymName = tenant?.Name ?? dto.GymName,
        GymNameAr = tenant?.NameAr ?? string.Empty,
        GymCode = tenant?.GymCode ?? string.Empty,
        Currency = tenant?.Currency ?? "EGP",
        ShiftId = dto.ShiftId,
        StaffName = dto.StaffName,
        OpenedAt = dto.OpenedAt,
        ClosedAt = dto.ClosedAt,
        Status = dto.Status,
        IsFinal = dto.IsFinal,
        RevealCash = dto.RevealCash,
        GrossSales = dto.GrossSales,
        Discounts = dto.Discounts,
        Refunds = dto.Refunds,
        NetSales = dto.NetSales,
        TransactionCount = dto.TransactionCount,
        Methods = dto.Methods
            .Select(m => new ZReportPdfMethodTotal { Method = m.Method, Count = m.Count, Total = m.Total })
            .ToList(),
        OpeningCash = dto.OpeningCash,
        CashSales = dto.CashSales,
        CashRefunds = dto.CashRefunds,
        CashExpenses = dto.CashExpenses,
        CashPaidIn = dto.CashPaidIn,
        FloatAdjust = dto.FloatAdjust,
        ExpectedCash = dto.ExpectedCash,
        CountedCash = dto.CountedCash,
        Difference = dto.Difference,
        Memberships = dto.Memberships,
        MembershipCount = dto.MembershipCount,
        Renewals = dto.Renewals,
        RenewalCount = dto.RenewalCount,
        Products = dto.Products,
        ProductCount = dto.ProductCount,
        Other = dto.Other,
        OtherCount = dto.OtherCount,
        RefundCount = dto.RefundCount,
        DiscountCount = dto.DiscountCount
    };

    // ========================================================================
    // AGGREGATION
    // ========================================================================

    private async Task<ZReportPayload> ComputeAggregationAsync(Guid tenantId, DateOnly reportDate)
    {
        var (utcStart, utcEnd) = CairoBusinessDayUtcRange(reportDate);

        var sales = await _dbContext.Sales
            .Where(s => s.TenantId == tenantId && s.CreatedAtUtc >= utcStart && s.CreatedAtUtc < utcEnd)
            .ToListAsync();

        var saleIds = sales.Select(s => s.Id).ToList();

        var paymentTransactions = await _dbContext.PaymentTransactions
            .Where(p => p.TenantId == tenantId && p.SaleId != null && saleIds.Contains(p.SaleId.Value))
            .ToListAsync();

        var methodTotals = paymentTransactions
            .Where(p => !string.IsNullOrEmpty(p.Method))
            .GroupBy(p => p.Method!)
            .Select(g => new ZReportMethodTotalDto { Method = g.Key, Count = g.Count(), Total = g.Sum(p => p.Amount) })
            .OrderBy(m => m.Method)
            .ToList();

        var saleLines = await _dbContext.SaleLines
            .Where(l => l.TenantId == tenantId && saleIds.Contains(l.SaleId))
            .ToListAsync();

        var lineTypeTotals = saleLines
            .GroupBy(l => l.LineType)
            .Select(g => new ZReportLineTypeTotalDto { LineType = g.Key, Count = g.Count(), Revenue = g.Sum(l => l.LineTotal) })
            .OrderBy(l => l.LineType)
            .ToList();

        var promoDiscountTotal = sales.Sum(s => s.DiscountAmount);
        var manualDiscountTotal = sales.Sum(s => s.ManualDiscountAmount ?? 0m);
        var manualDiscountCount = sales.Count(s => s.ManualDiscountAmount.HasValue && s.ManualDiscountAmount.Value != 0);

        var outstandingAddedToday = sales
            .Where(s => s.Status == "partially_paid")
            .Sum(s => s.AmountDue);

        var membershipRevenueToday = lineTypeTotals
            .Where(l => l.LineType == "membership")
            .Sum(l => l.Revenue);

        var refundsTotal = await _dbContext.Refunds
            .Where(r => r.TenantId == tenantId && r.Status == "executed"
                     && r.ExecutedAt != null && r.ExecutedAt >= utcStart && r.ExecutedAt < utcEnd)
            .SumAsync(r => (decimal?)r.Amount) ?? 0m;

        var shifts = await _dbContext.Shifts
            .Include(s => s.User)
            .Where(s => s.TenantId == tenantId && s.OpenedAt >= utcStart && s.OpenedAt < utcEnd)
            .ToListAsync();

        var shiftRows = shifts
            .OrderBy(s => s.OpenedAt)
            .Select(s => new ZReportShiftRowDto
            {
                UserId = s.UserId,
                UserName = s.User != null ? $"{s.User.FirstName} {s.User.LastName}" : string.Empty,
                OpenedAt = s.OpenedAt,
                ClosedAt = s.ClosedAt,
                OpeningFloat = s.OpeningFloat,
                ExpectedCash = s.ExpectedCash,
                CountedCash = s.CountedCash,
                Variance = s.Variance,
                Status = s.Status
            })
            .ToList();

        return new ZReportPayload
        {
            MethodTotals = methodTotals,
            LineTypeTotals = lineTypeTotals,
            PromoDiscountTotal = promoDiscountTotal,
            ManualDiscountTotal = manualDiscountTotal,
            ManualDiscountCount = manualDiscountCount,
            RefundsTotal = refundsTotal,
            Shifts = shiftRows,
            OutstandingAddedToday = outstandingAddedToday,
            MembershipRevenueToday = membershipRevenueToday
        };
    }

    /// <summary>
    /// Converts a Cairo calendar day into the [start, end) UTC range that contains it — e.g.
    /// 2026-01-15 21:30 UTC (= 23:30 Cairo) falls in the 2026-01-15 report, while 2026-01-15 22:01
    /// UTC (= 00:01 Cairo the next day) falls in the 2026-01-16 report.
    /// </summary>
    private static (DateTime UtcStart, DateTime UtcEnd) CairoBusinessDayUtcRange(DateOnly reportDate)
    {
        var cairoLocalStart = DateTime.SpecifyKind(reportDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var cairoLocalEnd = DateTime.SpecifyKind(reportDate.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);

        var utcStart = TimeZoneInfo.ConvertTimeToUtc(cairoLocalStart, CairoTimeZone);
        var utcEnd = TimeZoneInfo.ConvertTimeToUtc(cairoLocalEnd, CairoTimeZone);

        return (utcStart, utcEnd);
    }

    private static Result<ZReportDto> Fail(string code, string message) =>
        Result<ZReportDto>.Failure($"{code}|{message}");

    private static ZReportDto MapToDto(ZReport entity)
    {
        var payload = JsonSerializer.Deserialize<ZReportPayload>(entity.PayloadJson) ?? new ZReportPayload();
        return MapToDto(entity, payload);
    }

    private static ZReportDto MapToDto(ZReport entity, ZReportPayload payload) => new()
    {
        Id = entity.Id,
        TenantId = entity.TenantId,
        ReportDate = entity.ReportDate,
        PdfUrl = entity.PdfUrl,
        GeneratedAt = entity.GeneratedAt,
        GeneratedByUserId = entity.GeneratedByUserId,
        MethodTotals = payload.MethodTotals,
        LineTypeTotals = payload.LineTypeTotals,
        PromoDiscountTotal = payload.PromoDiscountTotal,
        ManualDiscountTotal = payload.ManualDiscountTotal,
        ManualDiscountCount = payload.ManualDiscountCount,
        RefundsTotal = payload.RefundsTotal,
        Shifts = payload.Shifts,
        OutstandingAddedToday = payload.OutstandingAddedToday,
        MembershipRevenueToday = payload.MembershipRevenueToday
    };

    private static ZReportPdfModel BuildPdfModel(ZReportDto dto, Tenant? tenant) => new()
    {
        ReportDate = dto.ReportDate,
        GeneratedAt = dto.GeneratedAt,
        TenantName = tenant?.Name ?? string.Empty,
        TenantNameAr = tenant?.NameAr ?? string.Empty,
        GymCode = tenant?.GymCode ?? string.Empty,
        Currency = tenant?.Currency ?? "EGP",
        MethodTotals = dto.MethodTotals
            .Select(m => new ZReportPdfMethodTotal { Method = m.Method, Count = m.Count, Total = m.Total })
            .ToList(),
        LineTypeTotals = dto.LineTypeTotals
            .Select(l => new ZReportPdfLineTypeTotal { LineType = l.LineType, Count = l.Count, Revenue = l.Revenue })
            .ToList(),
        PromoDiscountTotal = dto.PromoDiscountTotal,
        ManualDiscountTotal = dto.ManualDiscountTotal,
        ManualDiscountCount = dto.ManualDiscountCount,
        RefundsTotal = dto.RefundsTotal,
        Shifts = dto.Shifts
            .Select(s => new ZReportPdfShiftRow
            {
                UserId = s.UserId,
                UserName = s.UserName,
                OpenedAt = s.OpenedAt,
                ClosedAt = s.ClosedAt,
                OpeningFloat = s.OpeningFloat,
                ExpectedCash = s.ExpectedCash,
                CountedCash = s.CountedCash,
                Variance = s.Variance,
                Status = s.Status
            })
            .ToList(),
        OutstandingAddedToday = dto.OutstandingAddedToday,
        MembershipRevenueToday = dto.MembershipRevenueToday
    };

    /// <summary>The exact shape persisted into ZReport.PayloadJson — everything BuildAsync/RegenerateAsync
    /// compute, minus the entity's own metadata columns (Id/TenantId/ReportDate/PdfUrl/GeneratedAt/GeneratedByUserId).</summary>
    private class ZReportPayload
    {
        public List<ZReportMethodTotalDto> MethodTotals { get; set; } = new();
        public List<ZReportLineTypeTotalDto> LineTypeTotals { get; set; } = new();
        public decimal PromoDiscountTotal { get; set; }
        public decimal ManualDiscountTotal { get; set; }
        public int ManualDiscountCount { get; set; }
        public decimal RefundsTotal { get; set; }
        public List<ZReportShiftRowDto> Shifts { get; set; } = new();
        public decimal OutstandingAddedToday { get; set; }
        public decimal MembershipRevenueToday { get; set; }
    }
}
