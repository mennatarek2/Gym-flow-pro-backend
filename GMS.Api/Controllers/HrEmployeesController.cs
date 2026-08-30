namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>HR Foundation: employees and their historical contracts.</summary>
[Route("api/hr/employees")]
[Authorize]
[FeatureFlag("hr")]
public class HrEmployeesController : BaseApiController
{
    private readonly IEmployeeService _employees;
    private readonly IEmployeeAppActivationService _employeeAppActivation;
    private readonly ITenantContext _tenantContext;

    public HrEmployeesController(
        IEmployeeService employees,
        IEmployeeAppActivationService employeeAppActivation,
        ITenantContext tenantContext)
    {
        _employees = employees;
        _employeeAppActivation = employeeAppActivation;
        _tenantContext = tenantContext;
    }

    /// <summary>Employee App self profile — resolves Employee from JWT. Never takes EmployeeId from client.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(EmployeeMeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMe()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var identityUserId = GetIdentityUserId();
        if (identityUserId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        var result = await _employees.GetMeAsync(_tenantContext.TenantId, identityUserId);
        if (!result.IsSuccess)
            return Forbid();
        return Ok(result.Data);
    }

    [HttpGet]
    [HasPermission(Permissions.HrView)]
    [ProducesResponseType(typeof(List<EmployeeListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? status = null, [FromQuery] Guid? departmentId = null, [FromQuery] string? search = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _employees.ListAsync(_tenantContext.TenantId, status, departmentId, search);
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.HrView)]
    [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _employees.GetAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost]
    [HasPermission(Permissions.HrManage)]
    [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _employees.CreateAsync(_tenantContext.TenantId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(Get), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.HrManage)]
    [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEmployeeRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _employees.UpdateAsync(_tenantContext.TenantId, id, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/terminate")]
    [HasPermission(Permissions.HrManage)]
    [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Terminate(Guid id, [FromBody] TerminateEmployeeRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _employees.TerminateAsync(_tenantContext.TenantId, id, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/photo")]
    [HasPermission(Permissions.HrManage)]
    [RequestSizeLimit(2 * 1024 * 1024)]
    [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadPhoto(Guid id, IFormFile? file)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No image uploaded / لم يتم رفع صورة" });
        if (file.Length > 2 * 1024 * 1024)
            return BadRequest(new { error = "Image must be ≤ 2MB / الصورة يجب ألا تتجاوز 2 ميجا" });

        var contentType = (file.ContentType ?? string.Empty).Trim().ToLowerInvariant();
        var isAllowed = contentType is "image/jpeg" or "image/jpg" or "image/png" or "image/webp" or "image/gif";
        if (!isAllowed)
            return BadRequest(new { error = "Only JPEG/PNG/WebP/GIF images / صور JPEG أو PNG أو WebP أو GIF فقط" });

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
        {
            extension = contentType switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                _ => ".jpg"
            };
        }

        var safeName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        await using var stream = file.OpenReadStream();
        var result = await _employees.SetPhotoAsync(_tenantContext.TenantId, id, stream, safeName, contentType);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}/contracts")]
    [HasPermission(Permissions.HrView)]
    [ProducesResponseType(typeof(List<EmployeeContractDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListContracts(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _employees.ListContractsAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/contracts")]
    [HasPermission(Permissions.HrManage)]
    [ProducesResponseType(typeof(EmployeeContractDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddContract(Guid id, [FromBody] CreateEmployeeContractRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _employees.AddContractAsync(_tenantContext.TenantId, id, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(ListContracts), new { id }, result.Data);
    }

    [HttpGet("{id:guid}/available-staff")]
    [HasPermission(Permissions.HrManage)]
    [ProducesResponseType(typeof(List<AvailableStaffDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAvailableStaff(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _employees.ListAvailableStaffAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/link-staff")]
    [HasPermission(Permissions.HrManage)]
    [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> LinkStaff(Guid id, [FromBody] LinkStaffRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _employees.LinkStaffAsync(_tenantContext.TenantId, id, request.AppUserId);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/unlink-staff")]
    [HasPermission(Permissions.HrManage)]
    [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnlinkStaff(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _employees.UnlinkStaffAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    /// <summary>
    /// Generate a one-time Employee App activation code (plaintext returned once).
    /// POST /api/hr/employees/{id}/app-activation-code
    /// </summary>
    [HttpPost("{id:guid}/app-activation-code")]
    [HasPermission(Permissions.HrManage)]
    [ProducesResponseType(typeof(EmployeeAppActivationCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateAppActivationCode(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var createdBy = GetIdentityUserId();
        var result = await _employeeAppActivation.GenerateAsync(id, createdBy == Guid.Empty ? null : createdBy);
        if (!result.IsSuccess)
        {
            if (result.Error!.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return NotFound(new { error = result.Error });
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Data);
    }

    private Guid GetIdentityUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
