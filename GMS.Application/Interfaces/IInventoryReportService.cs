namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;

public interface IInventoryReportService
{
    Task<Result<InventorySummaryReportDto>> GetSummaryAsync(Guid tenantId, bool includeValuation);

    Task<Result<List<InventoryMovementReportRowDto>>> GetMovementsAsync(
        Guid tenantId, InventoryMovementQueryRequest request);

    Task<Result<List<InventoryReorderSuggestionDto>>> GetReorderSuggestionsAsync(
        Guid tenantId, bool includeCost = false);

    Task<Result<List<InventoryDeadStockRowDto>>> GetDeadStockAsync(
        Guid tenantId, int daysIdle = 30, bool includeCost = false);

    Task<Result<List<InventoryProductPerformanceRowDto>>> GetProductPerformanceAsync(
        Guid tenantId, DateTime fromUtc, DateTime toUtc, bool includeMargin, int take = 50);

    /// <summary>Daily Hangfire entry: low-stock + expiry staff alerts with once-per-day dedupe.</summary>
    Task<Result<InventoryAlertJobResultDto>> RunDailyAlertsAsync(Guid tenantId, DateOnly cairoDate);
}
