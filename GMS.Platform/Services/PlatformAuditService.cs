namespace GMS.Platform.Services;

using System.Data;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Core.Attributes;
using GMS.Platform.DTOs;
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

    public async Task<PlatformPagedResult<PlatformAuditLogDto>> ListAsync(
        Guid? tenantId,
        string? action,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);

        var query = _db.PlatformAuditLogs.AsNoTracking().AsQueryable();
        if (tenantId.HasValue)
            query = query.Where(a => a.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(action))
        {
            var term = action.Trim();
            query = query.Where(a => a.Action.Contains(term));
        }
        if (from.HasValue)
        {
            var fromUtc = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(a => a.CreatedAtUtc >= fromUtc);
        }
        if (to.HasValue)
        {
            var toUtc = to.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(a => a.CreatedAtUtc <= toUtc);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var actorIds = rows.Select(a => a.ActorPlatformUserId).Distinct().ToList();
        var actorNames = await _db.PlatformAdminUsers.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        var tenantIds = rows.Where(a => a.TenantId.HasValue).Select(a => a.TenantId!.Value).Distinct().ToList();
        var tenantNames = await LoadTenantNamesAsync(tenantIds, cancellationToken);

        var items = rows.Select(a =>
        {
            TenantNameRow? tenantRow = null;
            if (a.TenantId.HasValue)
                tenantNames.TryGetValue(a.TenantId.Value, out tenantRow);

            return new PlatformAuditLogDto
            {
                Id = a.Id,
                ActorPlatformUserId = a.ActorPlatformUserId,
                ActorName = actorNames.TryGetValue(a.ActorPlatformUserId, out var name) ? name : null,
                Action = a.Action,
                TenantId = a.TenantId,
                TenantName = tenantRow?.Name,
                GymCode = tenantRow?.GymCode,
                BeforeJson = a.BeforeJson,
                AfterJson = a.AfterJson,
                CreatedAtUtc = a.CreatedAtUtc
            };
        }).ToList();

        return new PlatformPagedResult<PlatformAuditLogDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    private sealed record TenantNameRow(string Name, string GymCode);

    /// <summary>Raw SQL against dbo.tenants — PlatformDbContext deliberately has no EF model for
    /// tenant-side tables (mirrors PlatformTenantReadService's own tenant-name lookups).</summary>
    private async Task<Dictionary<Guid, TenantNameRow>> LoadTenantNamesAsync(
        List<Guid> tenantIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, TenantNameRow>();
        if (tenantIds.Count == 0 || !_db.Database.IsRelational())
            return result;

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        var paramNames = tenantIds.Select((_, i) => $"@t{i}").ToList();
        command.CommandText = $"SELECT Id, Name, GymCode FROM dbo.tenants WHERE Id IN ({string.Join(",", paramNames)})";
        for (var i = 0; i < tenantIds.Count; i++)
        {
            var param = command.CreateParameter();
            param.ParameterName = paramNames[i];
            param.Value = tenantIds[i];
            command.Parameters.Add(param);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(reader.GetOrdinal("Id"));
            result[id] = new TenantNameRow(
                reader["Name"]?.ToString() ?? string.Empty,
                reader["GymCode"]?.ToString() ?? string.Empty);
        }

        return result;
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
