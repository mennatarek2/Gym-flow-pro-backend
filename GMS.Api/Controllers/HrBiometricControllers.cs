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

/// <summary>Biometric device registry (generic foundation — no vendor hardware I/O).</summary>
[Route("api/hr/biometric-devices")]
[Authorize]
[FeatureFlag("hr")]
public class HrBiometricDevicesController : BaseApiController
{
    private readonly IBiometricDeviceService _devices;
    private readonly ITenantContext _tenantContext;
    private readonly IEmployeeAttendanceService _attendance;

    public HrBiometricDevicesController(
        IBiometricDeviceService devices,
        ITenantContext tenantContext,
        IEmployeeAttendanceService attendance)
    {
        _devices = devices;
        _tenantContext = tenantContext;
        _attendance = attendance;
    }

    [HttpGet]
    [HasPermission(Permissions.HrAttendanceView)]
    [ProducesResponseType(typeof(List<BiometricDeviceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        var result = await _devices.ListAsync(_tenantContext.TenantId);
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.HrAttendanceView)]
    [ProducesResponseType(typeof(BiometricDeviceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        var result = await _devices.GetAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost]
    [HasPermission(Permissions.HrAttendanceManage)]
    [ProducesResponseType(typeof(BiometricDeviceCreatedDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateBiometricDeviceRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        var actor = await ResolveActorAsync();
        var result = await _devices.CreateAsync(_tenantContext.TenantId, request, actor);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.HrAttendanceManage)]
    [ProducesResponseType(typeof(BiometricDeviceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBiometricDeviceRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        var actor = await ResolveActorAsync();
        var result = await _devices.UpdateAsync(_tenantContext.TenantId, id, request, actor);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/rotate-api-key")]
    [HasPermission(Permissions.HrAttendanceManage)]
    [ProducesResponseType(typeof(BiometricDeviceCreatedDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RotateKey(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        var actor = await ResolveActorAsync();
        var result = await _devices.RotateApiKeyAsync(_tenantContext.TenantId, id, actor);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    private async Task<Guid?> ResolveActorAsync()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var identityUserId) || identityUserId == Guid.Empty)
            return null;
        return await _attendance.ResolveAppUserIdForCallerAsync(_tenantContext.TenantId, identityUserId);
    }
}

/// <summary>Employee ↔ device-user mappings.</summary>
[Route("api/hr/biometric-mappings")]
[Authorize]
[FeatureFlag("hr")]
public class HrBiometricMappingsController : BaseApiController
{
    private readonly IBiometricMappingService _mappings;
    private readonly ITenantContext _tenantContext;

    public HrBiometricMappingsController(IBiometricMappingService mappings, ITenantContext tenantContext)
    {
        _mappings = mappings;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.HrAttendanceView)]
    [ProducesResponseType(typeof(List<BiometricEmployeeMappingDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? deviceId = null, [FromQuery] Guid? employeeId = null, [FromQuery] bool? enabledOnly = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        var result = await _mappings.ListAsync(_tenantContext.TenantId, deviceId, employeeId, enabledOnly);
        return Ok(result.Data);
    }

    [HttpPost]
    [HasPermission(Permissions.HrAttendanceManage)]
    [ProducesResponseType(typeof(BiometricEmployeeMappingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Upsert([FromBody] UpsertBiometricMappingRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        var result = await _mappings.UpsertAsync(_tenantContext.TenantId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/disable")]
    [HasPermission(Permissions.HrAttendanceManage)]
    [ProducesResponseType(typeof(BiometricEmployeeMappingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Disable(Guid id, [FromBody] DisableMappingRequest? request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        var result = await _mappings.DisableAsync(_tenantContext.TenantId, id, request?.Reason ?? "Manual disable");
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }
}

public class DisableMappingRequest
{
    public string? Reason { get; set; }
}

/// <summary>Raw biometric event review (staff). Push ingest is a separate controller.</summary>
[Route("api/hr/biometric-events")]
[Authorize]
[FeatureFlag("hr")]
public class HrBiometricEventsController : BaseApiController
{
    private readonly IBiometricEventService _events;
    private readonly ITenantContext _tenantContext;

    public HrBiometricEventsController(IBiometricEventService events, ITenantContext tenantContext)
    {
        _events = events;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.HrAttendanceView)]
    [ProducesResponseType(typeof(List<BiometricAttendanceEventDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] Guid? deviceId = null,
        [FromQuery] Guid? employeeId = null,
        [FromQuery] string? status = null)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        var result = await _events.ListAsync(_tenantContext.TenantId, fromUtc, toUtc, deviceId, employeeId, status);
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.HrAttendanceView)]
    [ProducesResponseType(typeof(BiometricAttendanceEventDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        var result = await _events.GetEventAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id:guid}/reprocess")]
    [HasPermission(Permissions.HrAttendanceManage)]
    [ProducesResponseType(typeof(BiometricAttendanceEventDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reprocess(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });
        var result = await _events.ReprocessAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }
}

/// <summary>
/// Push-to-Local ingest for registered biometric bridges.
/// Authenticated by device id + API key headers — not employee JWT.
/// Local Edition only. Never accepts fingerprint templates.
/// </summary>
[Route("api/local/biometric")]
[AllowAnonymous]
[RequireLocalEdition]
public class BiometricPushIngestController : BaseApiController
{
    public const string DeviceIdHeader = "X-HyMotion-Device-Id";
    public const string DeviceKeyHeader = "X-HyMotion-Device-Key";

    private readonly IBiometricEventService _events;
    private readonly ILogger<BiometricPushIngestController> _logger;

    public BiometricPushIngestController(IBiometricEventService events, ILogger<BiometricPushIngestController> logger)
    {
        _events = events;
        _logger = logger;
    }

    /// <summary>POST /api/local/biometric/events</summary>
    [HttpPost("events")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("checkin-policy")]
    [ProducesResponseType(typeof(BiometricPushIngestResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ingest([FromBody] BiometricPushBatchRequest? request)
    {
        if (!Request.Headers.TryGetValue(DeviceIdHeader, out var idRaw)
            || !Guid.TryParse(idRaw.ToString(), out var deviceId))
            return Unauthorized(new { error = "Missing or invalid X-HyMotion-Device-Id." });

        if (!Request.Headers.TryGetValue(DeviceKeyHeader, out var keyRaw)
            || string.IsNullOrWhiteSpace(keyRaw.ToString()))
            return Unauthorized(new { error = "Missing X-HyMotion-Device-Key." });

        // Reject obvious template-looking payloads if a client mistakenly posts them.
        if (request?.Events != null
            && request.Events.Any(e =>
                (e.SafePayloadJson?.Contains("fingerprintTemplate", StringComparison.OrdinalIgnoreCase) ?? false)
                || (e.SafePayloadJson?.Contains("templateData", StringComparison.OrdinalIgnoreCase) ?? false)))
        {
            _logger.LogWarning("Biometric push rejected: payload appears to contain fingerprint template data");
            return BadRequest(new { error = "Fingerprint templates are not accepted. Send attendance events only." });
        }

        var result = await _events.IngestPushAsync(deviceId, keyRaw.ToString(), request ?? new BiometricPushBatchRequest());
        if (!result.IsSuccess)
        {
            if (result.Error?.Contains("credentials", StringComparison.OrdinalIgnoreCase) == true
                || result.Error?.Contains("Unknown device", StringComparison.OrdinalIgnoreCase) == true)
                return Unauthorized(new { error = result.Error });
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Data);
    }
}
