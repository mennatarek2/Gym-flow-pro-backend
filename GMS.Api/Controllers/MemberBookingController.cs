namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Application.DTOs.Activities;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;

/// <summary>
/// Member App classes &amp; booking. Everything is scoped to the JWT member (sub → GymMember).
/// A member can never read or mutate another member's booking — ownership is re-checked server-side.
/// </summary>
[Route("api/member/activity-bookings")]
[Authorize(Policy = "AuthenticatedMember")]
public class MemberBookingController : BaseApiController
{
    private readonly IMemberBookingService _memberBookings;
    private readonly ITenantContext _tenantContext;

    public MemberBookingController(IMemberBookingService memberBookings, ITenantContext tenantContext)
    {
        _memberBookings = memberBookings;
        _tenantContext = tenantContext;
    }

    /// <summary>Discover: activities + facilities with live eligibility and remaining quota.</summary>
    [HttpGet("activities")]
    public async Task<IActionResult> ListActivities(CancellationToken ct)
    {
        var (tenantId, userId, error) = Resolve();
        if (error != null) return Unauthorized(new { error });

        var result = await _memberBookings.ListActivitiesAsync(tenantId, userId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    /// <summary>Upcoming bookable sessions (optionally per activity).</summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> ListSessions([FromQuery] Guid? activityId, [FromQuery] DateTime? fromUtc, CancellationToken ct)
    {
        var (tenantId, userId, error) = Resolve();
        if (error != null) return Unauthorized(new { error });

        var result = await _memberBookings.ListUpcomingSessionsAsync(tenantId, userId, activityId, fromUtc, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    /// <summary>Book a session. Entitlement/quota/capacity are enforced server-side.</summary>
    [HttpPost("sessions/{sessionId:guid}/book")]
    public async Task<IActionResult> Book(Guid sessionId, CancellationToken ct)
    {
        var (tenantId, userId, error) = Resolve();
        if (error != null) return Unauthorized(new { error });

        var result = await _memberBookings.BookAsync(tenantId, userId, sessionId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    /// <summary>Cancellation policy before confirming ("cancel before 6:00 PM to restore your credit").</summary>
    [HttpGet("{bookingId:guid}/cancel-policy")]
    public async Task<IActionResult> CancelPolicy(Guid bookingId, CancellationToken ct)
    {
        var (tenantId, userId, error) = Resolve();
        if (error != null) return Unauthorized(new { error });

        var result = await _memberBookings.GetCancelPolicyAsync(tenantId, userId, bookingId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    /// <summary>Cancel own booking. ≥2h before start → quota refunded; inside window → cancelled_late.</summary>
    [HttpPut("{bookingId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid bookingId, CancellationToken ct)
    {
        var (tenantId, userId, error) = Resolve();
        if (error != null) return Unauthorized(new { error });

        var result = await _memberBookings.CancelOwnAsync(tenantId, userId, bookingId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    /// <summary>My bookings (confirmation screen reads one via /{id}).</summary>
    [HttpGet]
    public async Task<IActionResult> MyBookings(CancellationToken ct)
    {
        var (tenantId, userId, error) = Resolve();
        if (error != null) return Unauthorized(new { error });

        var result = await _memberBookings.MyBookingsAsync(tenantId, userId, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    [HttpGet("{bookingId:guid}")]
    public async Task<IActionResult> MyBooking(Guid bookingId, CancellationToken ct)
    {
        var (tenantId, userId, error) = Resolve();
        if (error != null) return Unauthorized(new { error });

        var result = await _memberBookings.MyBookingAsync(tenantId, userId, bookingId, ct);
        return result.IsSuccess ? Ok(result.Data) : NotFound(new { error = result.Error });
    }

    private (Guid TenantId, Guid UserId, string? Error) Resolve()
    {
        if (!_tenantContext.IsInitialized)
            return (Guid.Empty, Guid.Empty, "Tenant context required.");
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var userId) || userId == Guid.Empty)
            return (_tenantContext.TenantId, Guid.Empty, "Please log in / يرجى تسجيل الدخول");
        return (_tenantContext.TenantId, userId, null);
    }
}
