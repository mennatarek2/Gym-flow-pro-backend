namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>HR Phase 5: payroll periods, calculation, approval, close, and lines.
/// Payroll is sensitive — every action here is gated behind hr.payroll.* (never hr.view/hr.manage),
/// and self-service only ever returns the caller's own lines.</summary>
[Route("api/hr/payroll-periods")]
[Authorize]
[FeatureFlag("hr")]
public class HrPayrollPeriodsController : BaseApiController
{
    private readonly IPayrollPeriodService _payroll;
    private readonly IPayrollPaymentService _payments;
    private readonly IEmployeeService _employees;
    private readonly ITenantContext _tenantContext;

    public HrPayrollPeriodsController(
        IPayrollPeriodService payroll,
        IPayrollPaymentService payments,
        IEmployeeService employees,
        ITenantContext tenantContext)
    {
        _payroll = payroll;
        _payments = payments;
        _employees = employees;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.HrPayrollView)]
    [ProducesResponseType(typeof(List<PayrollPeriodDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _payroll.ListAsync(_tenantContext.TenantId);
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.HrPayrollView)]
    [ProducesResponseType(typeof(PayrollPeriodDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _payroll.GetAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost]
    [HasPermission(Permissions.HrPayrollManage)]
    [ProducesResponseType(typeof(PayrollPeriodDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreatePayrollPeriodRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _payroll.CreateAsync(_tenantContext.TenantId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPost("{id:guid}/calculate")]
    [HasPermission(Permissions.HrPayrollManage)]
    [ProducesResponseType(typeof(PayrollPeriodDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Calculate(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _payroll.CalculateAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/approve")]
    [HasPermission(Permissions.HrPayrollApprove)]
    [ProducesResponseType(typeof(PayrollPeriodDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var actorAppUserId = await ResolveActingAppUserIdAsync();
        var result = await _payroll.ApproveAsync(_tenantContext.TenantId, id, actorAppUserId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/close")]
    [HasPermission(Permissions.HrPayrollApprove)]
    [ProducesResponseType(typeof(PayrollPeriodDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Close(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var actorAppUserId = await ResolveActingAppUserIdAsync();
        var result = await _payroll.CloseAsync(_tenantContext.TenantId, id, actorAppUserId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}/lines")]
    [HasPermission(Permissions.HrPayrollView)]
    [ProducesResponseType(typeof(List<PayrollLineDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListLines(Guid id, [FromQuery] Guid? employeeId = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _payroll.ListLinesAsync(_tenantContext.TenantId, id, employeeId);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}/payments")]
    [HasPermission(Permissions.HrPayrollView)]
    [ProducesResponseType(typeof(List<PayrollPaymentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPayments(Guid id, CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        var result = await _payments.ListAsync(_tenantContext.TenantId, id, ct);
        return result.IsSuccess ? Ok(result.Data) : NotFound(new { error = result.Error });
    }

    [HttpPost("{id:guid}/payments")]
    [HasPermission(Permissions.HrPayrollApprove)]
    [ProducesResponseType(typeof(PayrollPaymentDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreatePayment(
        Guid id,
        [FromBody] CreatePayrollPaymentRequest request,
        CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        var result = await _payments.CreateAsync(
            _tenantContext.TenantId, id, GetIdentityUserId(), request, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(ListPayments), new { id }, result.Data)
            : BadRequest(new { error = result.Error });
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(List<PayrollLineDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMine([FromQuery] int? year = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var identityUserId = GetIdentityUserId();
        if (identityUserId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var employeeId = await _employees.ResolveEmployeeIdForCallerAsync(_tenantContext.TenantId, identityUserId);
        if (employeeId == null)
            return Forbid();

        var result = await _payroll.ListLinesForEmployeeAsync(_tenantContext.TenantId, employeeId.Value, year);
        return Ok(result.Data);
    }

    private async Task<Guid?> ResolveActingAppUserIdAsync()
    {
        var identityUserId = GetIdentityUserId();
        return identityUserId == Guid.Empty ? null : await _employees.ResolveAppUserIdForCallerAsync(_tenantContext.TenantId, identityUserId);
    }

    private Guid GetIdentityUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
