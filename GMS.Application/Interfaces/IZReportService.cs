namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.ZReports;

/// <summary>
/// Daily closing Z-Report: builds an immutable per-tenant/day aggregation snapshot (payment-method
/// breakdown, sales by line type, discounts, shift reconciliation, outstanding balances) for the
/// Cairo (Egypt Standard Time) business day.
/// </summary>
public interface IZReportService
{
    /// <summary>
    /// Builds and stores the Z-Report for <paramref name="reportDate"/> if one doesn't already
    /// exist; otherwise returns the existing immutable snapshot unchanged (no recompute).
    /// </summary>
    Task<Result<ZReportDto>> BuildAsync(Guid tenantId, DateOnly reportDate, Guid? requestedByUserId = null);

    Task<Result<ZReportDto>> GetAsync(Guid tenantId, DateOnly reportDate);

    /// <summary>Manager+ only: recomputes and overwrites an existing Z-Report's snapshot. Audited.</summary>
    Task<Result<ZReportDto>> RegenerateAsync(Guid tenantId, DateOnly reportDate, Guid managerUserId);

    /// <summary>Renders the Z-Report PDF on demand from the stored snapshot (re-rendered fresh each
    /// call — cheap, deterministic — rather than requiring the nightly job's stored PdfUrl to exist).</summary>
    Task<Result<byte[]>> GetPdfBytesAsync(Guid tenantId, DateOnly reportDate);

    /// <summary>
    /// Shift closing document from shift-linked sales, payments, refunds, and cash movements.
    /// Closed/approved cash expected/counted/difference are the frozen Shift columns (not recomputed into the row).
    /// Open shifts omit expected/counted/difference (blind count).
    /// </summary>
    Task<Result<ShiftZReportDto>> GetShiftClosingAsync(Guid tenantId, Guid shiftId);

    Task<Result<ShiftZReportListDto>> ListShiftClosingsAsync(Guid tenantId, DateOnly fromDate, DateOnly toDate);

    Task<Result<byte[]>> GetShiftClosingPdfAsync(Guid tenantId, Guid shiftId);
}
