namespace GMS.Platform.Services;

using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using GMS.Core.Attributes;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;

/// <summary>
/// Platform audit writer — mirrors tenant AuditService redaction + never-break-caller semantics.
/// </summary>
public class PlatformAuditService : IPlatformAuditService
{
    private const string RedactedPlaceholder = "***REDACTED***";

    private readonly PlatformDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<PlatformAuditService> _logger;

    public PlatformAuditService(
        PlatformDbContext db,
        IHttpContextAccessor httpContextAccessor,
        ILogger<PlatformAuditService> logger)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task LogAsync(
        Guid actorPlatformUserId,
        string action,
        Guid? tenantId = null,
        object? before = null,
        object? after = null,
        string? ipAddress = null)
    {
        try
        {
            ipAddress ??= _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

            _db.PlatformAuditLogs.Add(new PlatformAuditLog
            {
                ActorPlatformUserId = actorPlatformUserId,
                Action = action,
                TenantId = tenantId,
                BeforeJson = SerializeRedacted(before),
                AfterJson = SerializeRedacted(after),
                IpAddress = ipAddress,
                CreatedAtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write platform audit event for action {Action}", action);
        }
    }

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
