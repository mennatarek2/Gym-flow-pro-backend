namespace GMS.Application.Services;

using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Audit;
using GMS.Application.Interfaces;
using GMS.Core.Attributes;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Append-only audit trail writer + reader.
/// </summary>
public class AuditService : IAuditService
{
    private const string RedactedPlaceholder = "***REDACTED***";

    private readonly GymFlowProDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        GymFlowProDbContext dbContext,
        IHttpContextAccessor httpContextAccessor,
        ITenantContext tenantContext,
        ILogger<AuditService> logger)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task LogAsync(
        string action,
        string? entityType = null,
        Guid? entityId = null,
        object? before = null,
        object? after = null,
        Guid? tenantIdOverride = null)
    {
        AuditEvent? pending = null;
        try
        {
            var tenantId = tenantIdOverride ?? _tenantContext.TenantId;
            if (tenantId == Guid.Empty)
            {
                _logger.LogDebug("Skipping audit {Action}: no tenant context", action);
                return;
            }

            var httpContext = _httpContextAccessor.HttpContext;

            pending = new AuditEvent
            {
                TenantId = tenantId,
                ActorUserId = await ResolveActorUserIdAsync(httpContext),
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                BeforeJson = SerializeRedacted(before),
                AfterJson = SerializeRedacted(after),
                IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
                ImpersonatedByPlatformUserId = ResolveImpersonatedBy(httpContext)
            };

            _dbContext.AuditEvents.Add(pending);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Detach failed insert so ChangeTracker poison cannot break the caller (e.g. member activate).
            // ObjectDisposedException means the context itself is gone — there is nothing to detach
            // and touching it would rethrow, violating the fire-and-forget-safe contract.
            if (pending != null && ex is not ObjectDisposedException)
            {
                var entry = _dbContext.Entry(pending);
                if (entry.State != EntityState.Detached)
                    entry.State = EntityState.Detached;
            }

            _logger.LogError(ex, "Failed to write audit event for action {Action}", action);
        }
    }

    public async Task<Result<PagedResult<AuditEventDto>>> GetAuditEventsAsync(Guid tenantId, AuditEventQueryRequest query)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var q = _dbContext.AuditEvents.Where(a => a.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(query.EntityType))
            q = q.Where(a => a.EntityType == query.EntityType);

        if (query.EntityId.HasValue)
            q = q.Where(a => a.EntityId == query.EntityId);

        if (!string.IsNullOrWhiteSpace(query.Action))
            q = q.Where(a => a.Action == query.Action);

        if (query.From.HasValue)
            q = q.Where(a => a.CreatedAtUtc >= query.From.Value);

        if (query.To.HasValue)
            q = q.Where(a => a.CreatedAtUtc <= query.To.Value);

        var totalCount = await q.CountAsync();

        var items = await q
            .OrderByDescending(a => a.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditEventDto
            {
                Id = a.Id,
                ActorUserId = a.ActorUserId,
                Action = a.Action,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                BeforeJson = a.BeforeJson,
                AfterJson = a.AfterJson,
                IpAddress = a.IpAddress,
                ImpersonatedByPlatformUserId = a.ImpersonatedByPlatformUserId,
                CreatedAtUtc = a.CreatedAtUtc
            })
            .ToListAsync();

        return Result<PagedResult<AuditEventDto>>.Success(new PagedResult<AuditEventDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    private static Guid? ResolveImpersonatedBy(HttpContext? httpContext)
    {
        var raw = httpContext?.User.FindFirst(ImpersonationClaims.ImpersonatedByPlatformUserId)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>
    /// Resolves app_users.Id (domain PK) for the current staff actor. The JWT "sub"/NameIdentifier claim
    /// is ApplicationUser.Id (Identity PK) — NOT the same as AppUser.Id — so this looks up the AppUser
    /// row via UserId (string form of the Identity id) + tenant, matching the pattern used in
    /// CheckinService.ProcessManualCheckinAsync. Returns null if there's no HTTP context, no valid
    /// sub claim, or no matching AppUser (e.g. member-only tokens).
    /// </summary>
    private async Task<Guid?> ResolveActorUserIdAsync(HttpContext? httpContext)
    {
        if (httpContext == null)
            return null;

        var sub = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(sub, out var identityUserId))
            return null;

        var identityIdStr = identityUserId.ToString();
        var tenantId = _tenantContext.TenantId;

        var appUser = await _dbContext.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == identityIdStr && u.TenantId == tenantId);

        return appUser?.Id;
    }

    /// <summary>
    /// Serializes an object's public properties to JSON, replacing any [Redact]-annotated property's
    /// value with a fixed placeholder. Works on the object's declared shape only (top-level properties).
    /// </summary>
    private static string? SerializeRedacted(object? value)
    {
        if (value == null)
            return null;

        var properties = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var snapshot = new Dictionary<string, object?>();

        foreach (var property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
                continue;

            var isRedacted = property.GetCustomAttribute<RedactAttribute>() != null;
            snapshot[property.Name] = isRedacted ? RedactedPlaceholder : property.GetValue(value);
        }

        return JsonSerializer.Serialize(snapshot);
    }
}
