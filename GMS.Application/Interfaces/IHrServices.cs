namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Hr;

public interface IDepartmentService
{
    Task<Result<List<DepartmentDto>>> ListAsync(Guid tenantId, bool includeInactive = false);
    Task<Result<DepartmentDto>> GetAsync(Guid tenantId, Guid id);
    Task<Result<DepartmentDto>> CreateAsync(Guid tenantId, CreateDepartmentRequest request);
    Task<Result<DepartmentDto>> UpdateAsync(Guid tenantId, Guid id, UpdateDepartmentRequest request);
}

public interface IPositionService
{
    Task<Result<List<PositionDto>>> ListAsync(Guid tenantId, bool includeInactive = false, Guid? departmentId = null);
    Task<Result<PositionDto>> GetAsync(Guid tenantId, Guid id);
    Task<Result<PositionDto>> CreateAsync(Guid tenantId, CreatePositionRequest request);
    Task<Result<PositionDto>> UpdateAsync(Guid tenantId, Guid id, UpdatePositionRequest request);
}

public interface IEmployeeShiftService
{
    Task<Result<List<EmployeeShiftDto>>> ListAsync(Guid tenantId, bool includeInactive = false);
    Task<Result<EmployeeShiftDto>> GetAsync(Guid tenantId, Guid id);
    Task<Result<EmployeeShiftDto>> CreateAsync(Guid tenantId, CreateEmployeeShiftRequest request);
    Task<Result<EmployeeShiftDto>> UpdateAsync(Guid tenantId, Guid id, UpdateEmployeeShiftRequest request);
}

public interface IEmployeeScheduleService
{
    Task<Result<EmployeeScheduleAssignmentDto>> AssignAsync(Guid tenantId, AssignScheduleRequest request);
    Task<Result<bool>> RemoveAsync(Guid tenantId, Guid employeeId, DateOnly date);
    Task<Result<List<EmployeeScheduleAssignmentDto>>> ListAsync(Guid tenantId, DateOnly from, DateOnly to, Guid? employeeId = null);
    Task<Result<BulkAssignResultDto>> BulkAssignAsync(Guid tenantId, BulkAssignScheduleRequest request);
}

public interface IEmployeeAttendanceService
{
    Task<Result<EmployeeAttendanceDto>> CheckInAsync(Guid tenantId, Guid employeeId, string? notes, string source, Guid? actorAppUserId);
    Task<Result<EmployeeAttendanceDto>> CheckOutAsync(Guid tenantId, Guid employeeId, Guid? actorAppUserId);
    Task<Result<List<EmployeeAttendanceDto>>> ListAsync(Guid tenantId, DateOnly from, DateOnly to, Guid? employeeId = null, string? status = null);
    Task<Result<EmployeeAttendanceDto>> CorrectAsync(Guid tenantId, Guid attendanceId, CorrectAttendanceRequest request, Guid? actorAppUserId);

    /// <summary>Resolves the caller's own Employee.Id from their JWT identity (sub -> AppUser -> Employee.AppUserId).
    /// Returns null when the caller has no linked Employee row (e.g. a login not tied to any employee).</summary>
    Task<Guid?> ResolveEmployeeIdForCallerAsync(Guid tenantId, Guid identityUserId);

    /// <summary>Resolves the caller's AppUser.Id from their JWT identity (sub -> AppUser), for stamping
    /// CreatedByAppUserId on records created/corrected by any staff member — not only employees with
    /// their own Employee row (e.g. a Manager checking someone else in).</summary>
    Task<Guid?> ResolveAppUserIdForCallerAsync(Guid tenantId, Guid identityUserId);
}

public interface ILeaveBalanceService
{
    Task<Result<List<LeaveBalanceDto>>> ListAsync(Guid tenantId, Guid employeeId, int? year = null);
    Task<Result<LeaveBalanceDto>> SetEntitlementAsync(Guid tenantId, Guid employeeId, string leaveType, int year, decimal entitledDays);

    /// <summary>Loads or creates (tracked, NOT saved) the balance row for the same DbContext instance a
    /// caller (LeaveRequestService) is already using, so approve/cancel can flush the status change,
    /// the balance update, and any attendance rows in one atomic SaveChangesAsync — mirrors
    /// IWarehouseService.GetOrCreateDefaultAsync's precedent of a service interface returning a
    /// tracked entity for exactly this kind of same-transaction composition.</summary>
    Task<Result<GMS.Core.Entities.LeaveBalance>> GetOrCreateBalanceAsync(Guid tenantId, Guid employeeId, string leaveType, int year);
}

public interface ILeaveRequestService
{
    Task<Result<List<LeaveRequestDto>>> ListAsync(Guid tenantId, Guid? employeeId = null, string? status = null, DateOnly? from = null, DateOnly? to = null);
    Task<Result<LeaveRequestDto>> GetAsync(Guid tenantId, Guid id);
    Task<Result<LeaveRequestDto>> CreateAsync(Guid tenantId, Guid employeeId, CreateLeaveRequestRequest request);
    Task<Result<LeaveRequestDto>> ApproveAsync(Guid tenantId, Guid id, Guid? reviewerAppUserId, string? notes);
    Task<Result<LeaveRequestDto>> RejectAsync(Guid tenantId, Guid id, Guid? reviewerAppUserId, string? notes);
    Task<Result<LeaveRequestDto>> CancelAsync(Guid tenantId, Guid id, Guid? actorAppUserId, bool isSelfService, Guid? selfEmployeeId);
}

public interface IPayrollPeriodService
{
    Task<Result<List<PayrollPeriodDto>>> ListAsync(Guid tenantId);
    Task<Result<PayrollPeriodDto>> GetAsync(Guid tenantId, Guid id);
    Task<Result<PayrollPeriodDto>> CreateAsync(Guid tenantId, CreatePayrollPeriodRequest request);
    Task<Result<PayrollPeriodDto>> CalculateAsync(Guid tenantId, Guid id);
    Task<Result<PayrollPeriodDto>> ApproveAsync(Guid tenantId, Guid id, Guid? actorAppUserId);
    Task<Result<PayrollPeriodDto>> CloseAsync(Guid tenantId, Guid id, Guid? actorAppUserId);
    Task<Result<List<PayrollLineDto>>> ListLinesAsync(Guid tenantId, Guid periodId, Guid? employeeId = null);
    Task<Result<List<PayrollLineDto>>> ListLinesForEmployeeAsync(Guid tenantId, Guid employeeId, int? year = null);
}

public interface IPayrollAdjustmentService
{
    Task<Result<List<PayrollAdjustmentDto>>> ListAsync(Guid tenantId, Guid periodId, Guid? employeeId = null);
    Task<Result<PayrollAdjustmentDto>> CreateAsync(Guid tenantId, Guid periodId, CreatePayrollAdjustmentRequest request, Guid? actorAppUserId);
}

public interface IEmployeeDocumentService
{
    Task<Result<List<EmployeeDocumentDto>>> ListAsync(Guid tenantId, Guid employeeId);

    /// <summary>Cross-employee listing for the Documents desk and expiry browsing (optional
    /// expiryStatus filter: Valid | ExpiringSoon | Expired).</summary>
    Task<Result<List<EmployeeDocumentDto>>> ListAllAsync(Guid tenantId, string? expiryStatus = null);
    Task<Result<EmployeeDocumentDto>> UploadAsync(Guid tenantId, Guid employeeId, Stream file, string fileName, string contentType, CreateEmployeeDocumentRequest request, Guid? actorAppUserId);
    Task<Result<bool>> DeleteAsync(Guid tenantId, Guid documentId, Guid? actorAppUserId);

    /// <summary>Streams the raw file bytes for a protected download — never returns FileUrl directly
    /// to the frontend. When <paramref name="restrictToEmployeeId"/> is set (self-service), the
    /// document must belong to that employee or this fails, even if the tenant/id otherwise match.</summary>
    Task<Result<(byte[] Bytes, string ContentType, string FileName)>> DownloadAsync(Guid tenantId, Guid documentId, Guid? restrictToEmployeeId = null);
}

public interface IHrDashboardService
{
    Task<Result<HrDashboardDto>> GetAsync(Guid tenantId, bool includePayroll);
}

public interface IEmployeeService
{
    Task<Result<List<EmployeeListItemDto>>> ListAsync(Guid tenantId, string? status = null, Guid? departmentId = null, string? search = null);
    Task<Result<EmployeeDto>> GetAsync(Guid tenantId, Guid id);
    Task<Result<EmployeeDto>> CreateAsync(Guid tenantId, CreateEmployeeRequest request);
    Task<Result<EmployeeDto>> UpdateAsync(Guid tenantId, Guid id, UpdateEmployeeRequest request);
    Task<Result<EmployeeDto>> TerminateAsync(Guid tenantId, Guid id, TerminateEmployeeRequest request);
    Task<Result<EmployeeDto>> SetPhotoAsync(Guid tenantId, Guid id, Stream image, string fileName, string contentType);
    Task<Result<List<EmployeeContractDto>>> ListContractsAsync(Guid tenantId, Guid employeeId);
    Task<Result<EmployeeContractDto>> AddContractAsync(Guid tenantId, Guid employeeId, CreateEmployeeContractRequest request);

    /// <summary>The employee's contract that is Active as of today (StartDate&lt;=today, EndDate null or
    /// &gt;=today). For UI/profile display of "current salary right now". Null if none.</summary>
    Task<Result<EmployeeContractDto?>> GetCurrentContractAsync(Guid tenantId, Guid employeeId);

    /// <summary>The employee's contract that was Active as of an arbitrary reference date (StartDate&lt;=asOfDate,
    /// EndDate null or &gt;=asOfDate). Payroll uses this with the payroll period's own date range so a period
    /// is calculated from the contract that applied *during that period*, not whatever is current on the day
    /// someone happens to click Calculate. Null if none.</summary>
    Task<Result<EmployeeContractDto?>> GetContractAsOfAsync(Guid tenantId, Guid employeeId, DateOnly asOfDate);

    /// <summary>Resolves the caller's own Employee.Id from JWT identity.
    /// Prefers Employee App link (<c>EmployeeAppUserId</c>), also accepts Staff link (<c>AppUserId</c>).
    /// Returns null when unlinked or Employee is not Active.</summary>
    Task<Guid?> ResolveEmployeeIdForCallerAsync(Guid tenantId, Guid identityUserId);

    /// <summary>Resolves the caller's AppUser.Id from their JWT identity (sub -> AppUser).</summary>
    Task<Guid?> ResolveAppUserIdForCallerAsync(Guid tenantId, Guid identityUserId);

    /// <summary>Staff accounts in this tenant eligible to link to this employee — i.e. not already
    /// linked to a different employee. Reuses IAdminService's staff list (single source of truth for
    /// Staff identity/role) rather than re-deriving it from AppUser/Identity directly.</summary>
    Task<Result<List<AvailableStaffDto>>> ListAvailableStaffAsync(Guid tenantId, Guid employeeId);

    /// <summary>Links this employee to an existing Staff account (by AppUser.Id). Fails if the AppUser
    /// doesn't exist in this tenant or is already linked to a different employee.</summary>
    Task<Result<EmployeeDto>> LinkStaffAsync(Guid tenantId, Guid employeeId, Guid appUserId);

    /// <summary>Clears the employee's AppUserId. Does not touch the Staff/AppUser account itself.
    /// Does not clear EmployeeAppUserId (Employee App identity).</summary>
    Task<Result<EmployeeDto>> UnlinkStaffAsync(Guid tenantId, Guid employeeId);

    /// <summary>Authenticated Employee App / Staff-linked self profile. Resolves via JWT; never takes EmployeeId from client.</summary>
    Task<Result<EmployeeMeDto>> GetMeAsync(Guid tenantId, Guid identityUserId);
}
