namespace GMS.Application.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Shifts;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Cash-drawer shift management: open/close with blind-count reconciliation, signed cash movement
/// tracking, and variance approval.
/// </summary>
public class ShiftService : IShiftService
{
    private readonly GymFlowProDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ILogger<ShiftService> _logger;

    public ShiftService(GymFlowProDbContext dbContext, IAuditService auditService, ILogger<ShiftService> logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<Result<ShiftDto>> OpenAsync(decimal openingFloat, Guid staffUserId, Guid tenantId)
    {
        try
        {
            var staffUser = await ResolveStaffUserAsync(staffUserId, tenantId);
            if (staffUser == null)
                return Fail(ShiftFailureReasons.StaffUserNotFound, "Staff user not found / المستخدم غير موجود");

            var existingOpen = await _dbContext.Shifts
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.UserId == staffUser.Id && s.Status == "open");

            if (existingOpen != null)
                return Fail(ShiftFailureReasons.ShiftAlreadyOpen,
                    "You already have an open shift / لديك وردية مفتوحة بالفعل");

            var shift = new Shift
            {
                TenantId = tenantId,
                UserId = staffUser.Id,
                OpenedAt = DateTime.UtcNow,
                OpeningFloat = openingFloat,
                Status = "open"
            };

            _dbContext.Shifts.Add(shift);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Shift opened: {ShiftId} for user {UserId} (float {Float})", shift.Id, staffUser.Id, openingFloat);

            return Result<ShiftDto>.Success(ToDto(shift, staffUser, new List<CashMovement>()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening shift for tenant {TenantId}", tenantId);
            return Result<ShiftDto>.Failure("Failed to open shift / فشل فتح الوردية", ex.Message);
        }
    }

    public async Task<Result<ShiftDto>> GetCurrentAsync(Guid staffUserId, Guid tenantId)
    {
        try
        {
            var staffUser = await ResolveStaffUserAsync(staffUserId, tenantId);
            if (staffUser == null)
                return Fail(ShiftFailureReasons.StaffUserNotFound, "Staff user not found / المستخدم غير موجود");

            var shift = await _dbContext.Shifts
                .Include(s => s.Movements)
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.UserId == staffUser.Id && s.Status == "open");

            // No open shift is not a failure — the caller just has nothing current.
            return Result<ShiftDto>.Success(shift == null ? null! : ToDto(shift, staffUser, shift.Movements.ToList()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current shift for tenant {TenantId}", tenantId);
            return Result<ShiftDto>.Failure("Failed to retrieve current shift / فشل جلب الوردية الحالية", ex.Message);
        }
    }

    public async Task<Guid?> GetCurrentOpenShiftIdAsync(Guid staffUserId, Guid tenantId)
    {
        var staffUser = await ResolveStaffUserAsync(staffUserId, tenantId);
        if (staffUser == null)
            return null;

        return await _dbContext.Shifts
            .Where(s => s.TenantId == tenantId && s.Status == "open" && s.UserId == staffUser.Id)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<Result<CashMovementDto>> RecordMovementAsync(
        Guid shiftId, string type, decimal amount, Guid? referenceId, string? reason,
        Guid staffUserId, Guid tenantId, IReadOnlySet<string>? callerPermissions = null)
    {
        try
        {
            var validTypes = new[] { "sale", "refund", "paid_in", "paid_out", "float_adjust" };
            if (!validTypes.Contains(type))
                return CashMovementFail(ShiftFailureReasons.InvalidMovementType, "Invalid movement type / نوع الحركة غير صالح");

            var staffUser = await ResolveStaffUserAsync(staffUserId, tenantId);
            if (staffUser == null)
                return CashMovementFail(ShiftFailureReasons.StaffUserNotFound, "Staff user not found / المستخدم غير موجود");

            var shift = await _dbContext.Shifts
                .FirstOrDefaultAsync(s => s.Id == shiftId && s.TenantId == tenantId);

            if (shift == null || shift.UserId != staffUser.Id || shift.Status != "open")
                return CashMovementFail(ShiftFailureReasons.NoOpenShift, "No matching open shift / لا توجد وردية مفتوحة مطابقة");

            var signedAmount = NormalizeSignedAmount(type, amount);

            if (type == "paid_out")
            {
                var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
                var threshold = GetSettingDecimal(tenant?.Settings, TenantSettingsKeys.PaidOutApprovalThresholdEgp);

                if (threshold.HasValue && Math.Abs(signedAmount) > threshold.Value)
                {
                    var hasApprovalPermission = callerPermissions?.Contains(Permissions.ShiftReconcileApprove) ?? false;
                    if (!hasApprovalPermission)
                        return CashMovementFail(ShiftFailureReasons.ManagerApprovalRequired,
                            "A paid-out above the approval threshold requires manager approval / صرف نقدي يتجاوز الحد يتطلب موافقة المدير");
                }
            }

            var movement = new CashMovement
            {
                TenantId = tenantId,
                ShiftId = shiftId,
                Type = type,
                Amount = signedAmount,
                ReferenceId = referenceId,
                Reason = reason,
                CreatedByUserId = staffUser.Id
            };

            _dbContext.CashMovements.Add(movement);
            await _dbContext.SaveChangesAsync();

            return Result<CashMovementDto>.Success(ToMovementDto(movement));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording cash movement for shift {ShiftId}", shiftId);
            return Result<CashMovementDto>.Failure("Failed to record cash movement / فشل تسجيل الحركة النقدية", ex.Message);
        }
    }

    public async Task<Result<ShiftDto>> CloseAsync(decimal countedCash, string? varianceNote, Guid staffUserId, Guid tenantId)
    {
        try
        {
            var staffUser = await ResolveStaffUserAsync(staffUserId, tenantId);
            if (staffUser == null)
                return Fail(ShiftFailureReasons.StaffUserNotFound, "Staff user not found / المستخدم غير موجود");

            var shift = await _dbContext.Shifts
                .Include(s => s.Movements)
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.UserId == staffUser.Id && s.Status == "open");

            if (shift == null)
                return Fail(ShiftFailureReasons.NoOpenShift, "No open shift to close / لا توجد وردية مفتوحة لإغلاقها");

            var expectedCash = shift.OpeningFloat + shift.Movements.Sum(m => m.Amount);
            var variance = countedCash - expectedCash;

            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            var tolerance = GetSettingDecimal(tenant?.Settings, TenantSettingsKeys.VarianceToleranceEgp) ?? 20m;

            shift.ClosedAt = DateTime.UtcNow;
            shift.CountedCash = countedCash;
            shift.ExpectedCash = expectedCash;
            shift.Variance = variance;
            shift.VarianceNote = varianceNote;
            shift.Status = Math.Abs(variance) <= tolerance ? "approved" : "closed";
            shift.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Shift closed: {ShiftId} expected={Expected} counted={Counted} variance={Variance} status={Status}",
                shift.Id, expectedCash, countedCash, variance, shift.Status);

            return Result<ShiftDto>.Success(ToDto(shift, staffUser, shift.Movements.ToList()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing shift for tenant {TenantId}", tenantId);
            return Result<ShiftDto>.Failure("Failed to close shift / فشل إغلاق الوردية", ex.Message);
        }
    }

    public async Task<Result<ShiftDto>> ApproveAsync(Guid shiftId, string? note, Guid approverUserId, Guid tenantId)
    {
        try
        {
            var approver = await ResolveStaffUserAsync(approverUserId, tenantId);
            if (approver == null)
                return Fail(ShiftFailureReasons.StaffUserNotFound, "Staff user not found / المستخدم غير موجود");

            var shift = await _dbContext.Shifts
                .Include(s => s.Movements)
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Id == shiftId && s.TenantId == tenantId);

            if (shift == null)
                return Fail(ShiftFailureReasons.ShiftNotFound, "Shift not found / الوردية غير موجودة");

            if (shift.Status != "closed")
                return Fail(ShiftFailureReasons.NotAwaitingApproval,
                    "This shift is not awaiting approval / هذه الوردية ليست بانتظار الموافقة");

            shift.Status = "approved";
            shift.ApprovedByUserId = approver.Id;
            if (!string.IsNullOrWhiteSpace(note))
                shift.VarianceNote = note;
            shift.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync("shift.variance.approve", "Shift", shiftId, null,
                new { variance = shift.Variance, note });

            return Result<ShiftDto>.Success(ToDto(shift, shift.User, shift.Movements.ToList()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving shift {ShiftId}", shiftId);
            return Result<ShiftDto>.Failure("Failed to approve shift / فشل اعتماد الوردية", ex.Message);
        }
    }

    public async Task<Result<ShiftDto>> ForceCloseAsync(Guid shiftId, Guid managerUserId, Guid tenantId)
    {
        try
        {
            var manager = await ResolveStaffUserAsync(managerUserId, tenantId);
            if (manager == null)
                return Fail(ShiftFailureReasons.StaffUserNotFound, "Staff user not found / المستخدم غير موجود");

            var shift = await _dbContext.Shifts
                .Include(s => s.Movements)
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Id == shiftId && s.TenantId == tenantId);

            if (shift == null)
                return Fail(ShiftFailureReasons.ShiftNotFound, "Shift not found / الوردية غير موجودة");

            if (shift.Status != "open")
                return Fail(ShiftFailureReasons.ShiftNotOpen, "This shift is not open / هذه الوردية ليست مفتوحة");

            var expectedCash = shift.OpeningFloat + shift.Movements.Sum(m => m.Amount);

            shift.ClosedAt = DateTime.UtcNow;
            shift.ExpectedCash = expectedCash;
            shift.CountedCash = null;
            shift.Variance = null;
            shift.Status = "closed"; // still awaiting a real reconciliation/approval later
            shift.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync("shift.force_close", "Shift", shiftId, null,
                new { forcedByUserId = manager.Id, expectedCash });

            _logger.LogWarning("Shift {ShiftId} force-closed by {ManagerId}", shiftId, manager.Id);

            return Result<ShiftDto>.Success(ToDto(shift, shift.User, shift.Movements.ToList()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error force-closing shift {ShiftId}", shiftId);
            return Result<ShiftDto>.Failure("Failed to force-close shift / فشل الإغلاق القسري للوردية", ex.Message);
        }
    }

    public async Task<Result<PagedResult<ShiftDto>>> GetPagedAsync(
        Guid tenantId, DateTime? from, DateTime? to, Guid? userId, int page, int pageSize)
    {
        try
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _dbContext.Shifts.Include(s => s.User).Where(s => s.TenantId == tenantId);

            if (from.HasValue)
                query = query.Where(s => s.OpenedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(s => s.OpenedAt <= to.Value);

            if (userId.HasValue)
                query = query.Where(s => s.UserId == userId.Value);

            var totalCount = await query.CountAsync();

            var shifts = await query
                .OrderByDescending(s => s.OpenedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = shifts.Select(s => ToDto(s, s.User, new List<CashMovement>())).ToList();

            return Result<PagedResult<ShiftDto>>.Success(new PagedResult<ShiftDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving shifts for tenant {TenantId}", tenantId);
            return Result<PagedResult<ShiftDto>>.Failure("Failed to retrieve shifts / فشل جلب الورديات", ex.Message);
        }
    }

    public async Task<Result<ShiftOpenSummaryDto>> GetOpenSummaryAsync(Guid tenantId)
    {
        try
        {
            var openShifts = await _dbContext.Shifts
                .Include(s => s.User)
                .Include(s => s.Movements)
                .Where(s => s.TenantId == tenantId && s.Status == "open")
                .ToListAsync();

            var dtos = openShifts.Select(s => ToDto(s, s.User, s.Movements.ToList())).ToList();
            var total = openShifts.Sum(s => s.OpeningFloat + s.Movements.Sum(m => m.Amount));

            return Result<ShiftOpenSummaryDto>.Success(new ShiftOpenSummaryDto
            {
                OpenShifts = dtos,
                TotalCashInDrawers = total
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving open shift summary for tenant {TenantId}", tenantId);
            return Result<ShiftOpenSummaryDto>.Failure("Failed to retrieve open shift summary / فشل جلب ملخص الورديات المفتوحة", ex.Message);
        }
    }

    // ========================================================================
    // PRIVATE HELPERS
    // ========================================================================

    private async Task<AppUser?> ResolveStaffUserAsync(Guid identityUserId, Guid tenantId)
    {
        var identityUserIdStr = identityUserId.ToString();
        return await _dbContext.AppUsers
            .FirstOrDefaultAsync(u => u.UserId == identityUserIdStr && u.TenantId == tenantId);
    }

    private static decimal NormalizeSignedAmount(string type, decimal amount)
    {
        var magnitude = Math.Abs(amount);
        return type switch
        {
            "sale" or "paid_in" => magnitude,
            "refund" or "paid_out" => -magnitude,
            "float_adjust" => amount, // caller controls direction explicitly
            _ => amount
        };
    }

    private static Result<ShiftDto> Fail(string code, string message) =>
        Result<ShiftDto>.Failure($"{code}|{message}");

    private static Result<CashMovementDto> CashMovementFail(string code, string message) =>
        Result<CashMovementDto>.Failure($"{code}|{message}");

    private static CashMovementDto ToMovementDto(CashMovement movement) => new()
    {
        Id = movement.Id,
        Type = movement.Type,
        Amount = movement.Amount,
        ReferenceId = movement.ReferenceId,
        Reason = movement.Reason,
        CreatedByUserId = movement.CreatedByUserId,
        CreatedAtUtc = movement.CreatedAtUtc
    };

    private static ShiftDto ToDto(Shift shift, AppUser? user, List<CashMovement> movements) => new()
    {
        Id = shift.Id,
        UserId = shift.UserId,
        UserName = user != null ? $"{user.FirstName} {user.LastName}" : null,
        OpenedAt = shift.OpenedAt,
        ClosedAt = shift.ClosedAt,
        OpeningFloat = shift.OpeningFloat,
        ExpectedCash = shift.ExpectedCash, // null until CloseAsync computes it — blind count preserved naturally
        CountedCash = shift.CountedCash,
        Variance = shift.Variance,
        VarianceNote = shift.VarianceNote,
        ApprovedByUserId = shift.ApprovedByUserId,
        Status = shift.Status,
        Movements = movements.Select(ToMovementDto).ToList()
    };

    private static decimal? GetSettingDecimal(string? settingsJson, string key)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            return doc.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDecimal()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
