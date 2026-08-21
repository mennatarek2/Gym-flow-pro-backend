namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Application.DTOs.Attendance;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>
/// Attendance endpoints for QR check-in, manual check-in, and member search.
/// Tenant resolution uses ambient <see cref="ITenantContext"/> (TenantMiddleware), not JWT claims alone.
/// </summary>
[Route("api/attendance")]
public class AttendanceController : BaseApiController
{
    private readonly ICheckinService _checkinService;
    private readonly IGymOccupancyService _occupancy;
    private readonly ITenantContext _tenantContext;

    public AttendanceController(
        ICheckinService checkinService,
        IGymOccupancyService occupancy,
        ITenantContext tenantContext)
    {
        _checkinService = checkinService;
        _occupancy = occupancy;
        _tenantContext = tenantContext;
    }

    [HttpPost("qr-checkin")]
    [Authorize(Policy = "AuthenticatedMember")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("checkin-policy")]
    [ProducesResponseType(typeof(QrCheckinResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> QrCheckin([FromBody] QrCheckinRequest request)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty)
            return Unauthorized(new { error = "يرجى تسجيل الدخول / Please log in" });

        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _checkinService.ProcessQrCheckinAsync(request, userId, _tenantContext.TenantId);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Data);
    }

    [HttpPost("manual-checkin")]
    [HasPermission(Permissions.CheckinManual)]
    [ProducesResponseType(typeof(ManualCheckinResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ManualCheckin([FromBody] ManualCheckinRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _checkinService.ProcessManualCheckinAsync(
            request, GetUserId(), _tenantContext.TenantId);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Data);
    }

    /// <summary>
    /// Desk access-card check-in — exact MemberNumber (MAC-P0 Phase 2).
    /// </summary>
    [HttpPost("barcode-checkin")]
    [HasPermission(Permissions.CheckinManual)]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("checkin-policy")]
    [ProducesResponseType(typeof(ManualCheckinResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> BarcodeCheckin([FromBody] BarcodeCheckinRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _checkinService.ProcessBarcodeCheckinAsync(
            request, GetUserId(), _tenantContext.TenantId);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Data);
    }

    [HttpGet("search-members")]
    [HasPermission(Permissions.CheckinManual)]
    [ProducesResponseType(typeof(List<MemberSearchResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchMembers([FromQuery] MemberSearchRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _checkinService.SearchMembersForCheckinAsync(request, _tenantContext.TenantId);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Data);
    }

    [HttpGet("today")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(List<TodayAttendanceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTodayAttendance([FromQuery] string filter = "all")
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _checkinService.GetTodayAttendanceAsync(_tenantContext.TenantId, filter);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Data);
    }

    /// <summary>
    /// Live gym occupancy derived from today's open attendance visits.
    /// GET /api/attendance/occupancy
    /// </summary>
    [HttpGet("occupancy")]
    [HasPermission(Permissions.MembersView)]
    [ProducesResponseType(typeof(GymOccupancyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOccupancy(CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _occupancy.GetOccupancyAsync(_tenantContext.TenantId, ct);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
