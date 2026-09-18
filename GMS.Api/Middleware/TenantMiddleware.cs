namespace GMS.Api.Middleware;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Multi-tenancy middleware — Layer 3 (Application Level).
/// Resolves tenant, then applies CP5 suspension gate:
///   - suspended + within check-in buffer → only attendance/check-in paths
///   - suspended + buffer expired → block all tenant API (including check-in)
/// Auth login paths are skipped here; AuthService blocks staff dashboard logins separately.
/// </summary>
public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMiddleware> _logger;
    private static readonly string[] SkipPaths =
    {
        "/api/auth/",
        "/api/local-setup",
        "/platform-api/",
        "/health",
        "/swagger",
        "/_framework"
    };

    /// <summary>Member-facing check-in surface — deliberately diverges from other admin APIs while suspended.</summary>
    private static readonly string[] CheckinAllowPrefixes =
    {
        "/api/attendance/qr-checkin",
        "/api/attendance/manual-checkin",
        "/api/attendance/search"
    };

    public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        GymFlowProDbContext dbContext,
        ISubscriptionAccessService subscriptionAccess,
        IConfiguration configuration)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

        if (SkipPaths.Any(sp => path.StartsWith(sp, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var gymCode = context.User?.FindFirst("gym_code")?.Value;

        if (string.IsNullOrWhiteSpace(gymCode))
        {
            gymCode = context.Request.Headers["X-Gym-Code"].FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(gymCode))
        {
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                _logger.LogWarning("Authenticated request without gym_code claim or header.");
                await WriteErrorResponse(context, 401, "Tenant context required. Provide gym_code claim or X-Gym-Code header.");
                return;
            }

            await _next(context);
            return;
        }

        // Do not cache gym_code → tenant Id. After New Gym / retire the old code must
        // fail immediately instead of injecting a ghost Id into member/card inserts.
        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.GymCode == gymCode && !t.IsDeleted);

        if (tenant == null)
        {
            _logger.LogWarning("Tenant not found for gym_code: {GymCode}", gymCode);
            await WriteErrorResponse(context, 401, "Invalid gym code. Sign out and run Local setup again.");
            return;
        }

        if (!tenant.IsActive)
        {
            _logger.LogWarning("Inactive tenant accessed: {GymCode}", gymCode);
            await WriteErrorResponse(context, 401, "This gym is currently inactive.");
            return;
        }

        var jwtTenantId = context.User?.FindFirst("tenant_id")?.Value;
        if (Guid.TryParse(jwtTenantId, out var claimedTenantId) && claimedTenantId != tenant.Id)
        {
            _logger.LogWarning(
                "JWT tenant_id {Claimed} does not match gym_code {GymCode} tenant {Actual}",
                claimedTenantId, gymCode, tenant.Id);
            await WriteErrorResponse(context, 401, "Gym session is out of date. Sign in again.");
            return;
        }

        tenantContext.SetTenant(tenant.Id, tenant.Name, tenant.TimeZone);

        // CP5 suspension gate (distinct from AuthService login block).
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var access = await subscriptionAccess.GetAsync(tenant.Id);
            if (access?.IsSuspended == true)
            {
                var bufferHours = configuration.GetValue("PlatformBilling:SuspensionCheckinBufferHours", 72);
                var bufferOk = access.SuspendedAtUtc.HasValue &&
                               DateTime.UtcNow < access.SuspendedAtUtc.Value.AddHours(bufferHours);
                var isCheckinPath = CheckinAllowPrefixes.Any(p =>
                    path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

                if (bufferOk && isCheckinPath)
                {
                    await _next(context);
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    title = "SUBSCRIPTION_SUSPENDED",
                    detail = "This gym subscription is suspended. Please pay the outstanding HyMotion invoice / الاشتراك موقوف — يرجى سداد فاتورة HyMotion",
                    checkinBufferActive = bufferOk
                }));
                return;
            }
        }

        await _next(context);
    }

    private static async Task WriteErrorResponse(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var response = JsonSerializer.Serialize(new { error = message });
        await context.Response.WriteAsync(response);
    }
}
