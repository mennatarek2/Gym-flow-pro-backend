namespace GMS.Application.Services;

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GMS.Application.Common;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Application.Options;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Push-to-Local biometric foundation: durable raw events, mapping, clock validation, and safe
/// first/last punch reconciliation into existing <see cref="EmployeeAttendance"/> rows.
/// Does not talk to hardware SDKs and never stores fingerprint templates.
/// </summary>
public class BiometricAttendanceService : IBiometricDeviceService, IBiometricMappingService, IBiometricEventService
{
    private readonly GymFlowProDbContext _db;
    private readonly IAuditService _audit;
    private readonly ITenantContext _tenantContext;
    private readonly BiometricAttendanceOptions _options;
    private readonly ILogger<BiometricAttendanceService> _logger;

    public BiometricAttendanceService(
        GymFlowProDbContext db,
        IAuditService audit,
        ITenantContext tenantContext,
        IOptions<BiometricAttendanceOptions> options,
        ILogger<BiometricAttendanceService> logger)
    {
        _db = db;
        _audit = audit;
        _tenantContext = tenantContext;
        _options = options.Value;
        _logger = logger;
    }

    // ── Devices ─────────────────────────────────────────────────────────────

    public async Task<Result<List<BiometricDeviceDto>>> ListAsync(Guid tenantId)
    {
        var devices = await _db.BiometricDevices.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .OrderBy(d => d.DisplayName)
            .ToListAsync();

        var counts = await _db.BiometricEmployeeMappings.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.IsEnabled)
            .GroupBy(m => m.BiometricDeviceId)
            .Select(g => new { DeviceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DeviceId, x => x.Count);

        return Result<List<BiometricDeviceDto>>.Success(
            devices.Select(d => MapDevice(d, counts.GetValueOrDefault(d.Id))).ToList());
    }

    public async Task<Result<BiometricDeviceDto>> GetAsync(Guid tenantId, Guid deviceId)
    {
        var d = await _db.BiometricDevices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.TenantId == tenantId);
        if (d == null)
            return Result<BiometricDeviceDto>.Failure("Device not found / الجهاز غير موجود");

        var count = await _db.BiometricEmployeeMappings.AsNoTracking()
            .CountAsync(m => m.TenantId == tenantId && m.BiometricDeviceId == deviceId && m.IsEnabled);
        return Result<BiometricDeviceDto>.Success(MapDevice(d, count));
    }

    public async Task<Result<BiometricDeviceCreatedDto>> CreateAsync(
        Guid tenantId, CreateBiometricDeviceRequest request, Guid? actorAppUserId)
    {
        var code = (request.DeviceCode ?? string.Empty).Trim();
        var name = (request.DisplayName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name))
            return Result<BiometricDeviceCreatedDto>.Failure("Device code and name are required / رمز واسم الجهاز مطلوبان");

        var integration = string.IsNullOrWhiteSpace(request.IntegrationType)
            ? BiometricIntegrationTypes.PushToLocal
            : request.IntegrationType.Trim();
        if (!BiometricIntegrationTypes.All.Contains(integration))
            return Result<BiometricDeviceCreatedDto>.Failure("Invalid integration type / نوع التكامل غير صالح");

        var exists = await _db.BiometricDevices.AsNoTracking()
            .AnyAsync(d => d.TenantId == tenantId && d.DeviceCode == code);
        if (exists)
            return Result<BiometricDeviceCreatedDto>.Failure("Device code already exists / رمز الجهاز موجود بالفعل");

        var (plaintext, hash, prefix) = GenerateApiKey();
        var entity = new BiometricDevice
        {
            TenantId = tenantId,
            DeviceCode = code,
            DisplayName = name,
            Vendor = Normalize(request.Vendor),
            Model = Normalize(request.Model),
            IntegrationType = integration,
            LocationLabel = Normalize(request.LocationLabel),
            SafeConfigJson = Normalize(request.SafeConfigJson, 4000),
            IsEnabled = true,
            HealthStatus = BiometricDeviceHealthStatuses.AwaitingEvents,
            ApiKeyHash = hash,
            ApiKeyPrefix = prefix,
            ApiKeyRotatedAtUtc = DateTime.UtcNow,
            CreatedByAppUserId = actorAppUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.BiometricDevices.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("biometric_device.create", "BiometricDevice", entity.Id, null,
            new { entity.DeviceCode, entity.DisplayName, entity.IntegrationType, entity.ApiKeyPrefix });

        var dto = MapDeviceCreated(entity, 0, plaintext);
        return Result<BiometricDeviceCreatedDto>.Success(dto);
    }

    public async Task<Result<BiometricDeviceDto>> UpdateAsync(
        Guid tenantId, Guid deviceId, UpdateBiometricDeviceRequest request, Guid? actorAppUserId)
    {
        var entity = await _db.BiometricDevices.FirstOrDefaultAsync(d => d.Id == deviceId && d.TenantId == tenantId);
        if (entity == null)
            return Result<BiometricDeviceDto>.Failure("Device not found / الجهاز غير موجود");

        var name = (request.DisplayName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
            return Result<BiometricDeviceDto>.Failure("Display name is required / اسم الجهاز مطلوب");

        var integration = string.IsNullOrWhiteSpace(request.IntegrationType)
            ? BiometricIntegrationTypes.PushToLocal
            : request.IntegrationType.Trim();
        if (!BiometricIntegrationTypes.All.Contains(integration))
            return Result<BiometricDeviceDto>.Failure("Invalid integration type / نوع التكامل غير صالح");

        var before = new { entity.DisplayName, entity.IsEnabled, entity.IntegrationType };
        entity.DisplayName = name;
        entity.Vendor = Normalize(request.Vendor);
        entity.Model = Normalize(request.Model);
        entity.IntegrationType = integration;
        entity.LocationLabel = Normalize(request.LocationLabel);
        entity.SafeConfigJson = Normalize(request.SafeConfigJson, 4000);
        entity.IsEnabled = request.IsEnabled;
        entity.HealthStatus = request.IsEnabled
            ? (entity.HealthStatus == BiometricDeviceHealthStatuses.Disabled
                ? BiometricDeviceHealthStatuses.AwaitingEvents
                : entity.HealthStatus)
            : BiometricDeviceHealthStatuses.Disabled;
        entity.UpdatedByAppUserId = actorAppUserId;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("biometric_device.update", "BiometricDevice", entity.Id, before,
            new { entity.DisplayName, entity.IsEnabled, entity.IntegrationType });

        var count = await _db.BiometricEmployeeMappings.CountAsync(m =>
            m.TenantId == tenantId && m.BiometricDeviceId == deviceId && m.IsEnabled);
        return Result<BiometricDeviceDto>.Success(MapDevice(entity, count));
    }

    public async Task<Result<BiometricDeviceCreatedDto>> RotateApiKeyAsync(
        Guid tenantId, Guid deviceId, Guid? actorAppUserId)
    {
        var entity = await _db.BiometricDevices.FirstOrDefaultAsync(d => d.Id == deviceId && d.TenantId == tenantId);
        if (entity == null)
            return Result<BiometricDeviceCreatedDto>.Failure("Device not found / الجهاز غير موجود");

        var (plaintext, hash, prefix) = GenerateApiKey();
        entity.ApiKeyHash = hash;
        entity.ApiKeyPrefix = prefix;
        entity.ApiKeyRotatedAtUtc = DateTime.UtcNow;
        entity.UpdatedByAppUserId = actorAppUserId;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("biometric_device.rotate_key", "BiometricDevice", entity.Id, null,
            new { entity.ApiKeyPrefix });

        var count = await _db.BiometricEmployeeMappings.CountAsync(m =>
            m.TenantId == tenantId && m.BiometricDeviceId == deviceId && m.IsEnabled);
        return Result<BiometricDeviceCreatedDto>.Success(MapDeviceCreated(entity, count, plaintext));
    }

    // ── Mappings ────────────────────────────────────────────────────────────

    public async Task<Result<List<BiometricEmployeeMappingDto>>> ListAsync(
        Guid tenantId, Guid? deviceId = null, Guid? employeeId = null, bool? enabledOnly = null)
    {
        var q = _db.BiometricEmployeeMappings.AsNoTracking().Where(m => m.TenantId == tenantId);
        if (deviceId.HasValue) q = q.Where(m => m.BiometricDeviceId == deviceId);
        if (employeeId.HasValue) q = q.Where(m => m.EmployeeId == employeeId);
        if (enabledOnly == true) q = q.Where(m => m.IsEnabled);

        var rows = await q.OrderByDescending(m => m.IsEnabled).ThenBy(m => m.DeviceUserId).ToListAsync();
        return Result<List<BiometricEmployeeMappingDto>>.Success(await MapMappingsAsync(tenantId, rows));
    }

    public async Task<Result<BiometricEmployeeMappingDto>> UpsertAsync(Guid tenantId, UpsertBiometricMappingRequest request)
    {
        var deviceUserId = (request.DeviceUserId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(deviceUserId))
            return Result<BiometricEmployeeMappingDto>.Failure("Device user ID is required / معرف مستخدم الجهاز مطلوب");

        var device = await _db.BiometricDevices.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.BiometricDeviceId && d.TenantId == tenantId);
        if (device == null)
            return Result<BiometricEmployeeMappingDto>.Failure("Device not found / الجهاز غير موجود");

        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.TenantId == tenantId);
        if (employee == null)
            return Result<BiometricEmployeeMappingDto>.Failure("Employee not found / الموظف غير موجود");

        var conflictUser = await _db.BiometricEmployeeMappings
            .FirstOrDefaultAsync(m =>
                m.TenantId == tenantId
                && m.BiometricDeviceId == request.BiometricDeviceId
                && m.DeviceUserId == deviceUserId
                && m.IsEnabled
                && m.EmployeeId != request.EmployeeId);
        if (conflictUser != null)
            return Result<BiometricEmployeeMappingDto>.Failure(
                "Device user ID is already mapped to another employee / معرف مستخدم الجهاز مربوط بموظف آخر");

        var existing = await _db.BiometricEmployeeMappings
            .FirstOrDefaultAsync(m =>
                m.TenantId == tenantId
                && m.BiometricDeviceId == request.BiometricDeviceId
                && m.EmployeeId == request.EmployeeId);

        if (existing == null)
        {
            existing = new BiometricEmployeeMapping
            {
                TenantId = tenantId,
                BiometricDeviceId = request.BiometricDeviceId,
                EmployeeId = request.EmployeeId,
                DeviceUserId = deviceUserId,
                IsEnabled = true,
                RemoteDisableStatus = BiometricRemoteDisableStatuses.NotSupported,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.BiometricEmployeeMappings.Add(existing);
        }
        else
        {
            existing.DeviceUserId = deviceUserId;
            existing.IsEnabled = true;
            existing.DisabledReason = null;
            existing.DisabledAtUtc = null;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Result<BiometricEmployeeMappingDto>.Failure(
                "Mapping conflict — check duplicate device user or employee mapping / تعارض في الربط");
        }

        await _audit.LogAsync("biometric_mapping.upsert", "BiometricEmployeeMapping", existing.Id, null,
            new { existing.EmployeeId, existing.BiometricDeviceId, existing.DeviceUserId });

        var mapped = await MapMappingsAsync(tenantId, new List<BiometricEmployeeMapping> { existing });
        return Result<BiometricEmployeeMappingDto>.Success(mapped[0]);
    }

    public async Task<Result<BiometricEmployeeMappingDto>> DisableAsync(Guid tenantId, Guid mappingId, string reason)
    {
        var mapping = await _db.BiometricEmployeeMappings
            .FirstOrDefaultAsync(m => m.Id == mappingId && m.TenantId == tenantId);
        if (mapping == null)
            return Result<BiometricEmployeeMappingDto>.Failure("Mapping not found / الربط غير موجود");

        mapping.IsEnabled = false;
        mapping.DisabledReason = string.IsNullOrWhiteSpace(reason) ? "Manual disable" : reason.Trim();
        mapping.DisabledAtUtc = DateTime.UtcNow;
        mapping.RemoteDisableStatus = BiometricRemoteDisableStatuses.NotSupported;
        mapping.RemoteDisableNote = "Local mapping disabled only — remote device disablement not confirmed (no verified adapter).";
        mapping.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("biometric_mapping.disable", "BiometricEmployeeMapping", mapping.Id, null,
            new { mapping.DisabledReason, mapping.RemoteDisableStatus });

        var mapped = await MapMappingsAsync(tenantId, new List<BiometricEmployeeMapping> { mapping });
        return Result<BiometricEmployeeMappingDto>.Success(mapped[0]);
    }

    public async Task DisableMappingsForEmployeeAsync(Guid tenantId, Guid employeeId, string reason)
    {
        var mappings = await _db.BiometricEmployeeMappings
            .Where(m => m.TenantId == tenantId && m.EmployeeId == employeeId && m.IsEnabled)
            .ToListAsync();
        if (mappings.Count == 0)
            return;

        foreach (var m in mappings)
        {
            m.IsEnabled = false;
            m.DisabledReason = reason;
            m.DisabledAtUtc = DateTime.UtcNow;
            m.RemoteDisableStatus = BiometricRemoteDisableStatuses.NotSupported;
            m.RemoteDisableNote = "Local mapping disabled due to HR status — remote device user not confirmed disabled.";
            m.RemoteDisableRequestedAtUtc = DateTime.UtcNow;
            m.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        foreach (var m in mappings)
        {
            await _audit.LogAsync("biometric_mapping.auto_disable", "BiometricEmployeeMapping", m.Id, null,
                new { m.EmployeeId, m.DisabledReason, m.RemoteDisableStatus });
        }
    }

    // ── Events / ingest ─────────────────────────────────────────────────────

    public async Task<Result<List<BiometricAttendanceEventDto>>> ListAsync(
        Guid tenantId, DateTime? fromUtc = null, DateTime? toUtc = null,
        Guid? deviceId = null, Guid? employeeId = null, string? status = null)
    {
        var q = _db.BiometricAttendanceEvents.AsNoTracking().Where(e => e.TenantId == tenantId);
        if (fromUtc.HasValue) q = q.Where(e => e.ReceivedAtUtc >= fromUtc);
        if (toUtc.HasValue) q = q.Where(e => e.ReceivedAtUtc <= toUtc);
        if (deviceId.HasValue) q = q.Where(e => e.BiometricDeviceId == deviceId);
        if (employeeId.HasValue) q = q.Where(e => e.ResolvedEmployeeId == employeeId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(e => e.ProcessingStatus == status);

        var rows = await q.OrderByDescending(e => e.DeviceTimestampUtc).Take(500).ToListAsync();
        return Result<List<BiometricAttendanceEventDto>>.Success(await MapEventsAsync(tenantId, rows));
    }

    public async Task<Result<BiometricAttendanceEventDto>> GetEventAsync(Guid tenantId, Guid eventId)
    {
        var row = await _db.BiometricAttendanceEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == eventId && e.TenantId == tenantId);
        if (row == null)
            return Result<BiometricAttendanceEventDto>.Failure("Event not found / الحدث غير موجود");
        var mapped = await MapEventsAsync(tenantId, new List<BiometricAttendanceEvent> { row });
        return Result<BiometricAttendanceEventDto>.Success(mapped[0]);
    }

    public async Task<Result<BiometricPushIngestResultDto>> IngestPushAsync(
        Guid deviceId, string apiKeyPlaintext, BiometricPushBatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(apiKeyPlaintext))
            return Result<BiometricPushIngestResultDto>.Failure("Device API key required / مفتاح الجهاز مطلوب");

        // Push auth looks up by Id + key hash without ambient tenant filter.
        var device = await _db.BiometricDevices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == deviceId && !d.IsDeleted);
        if (device == null)
            return Result<BiometricPushIngestResultDto>.Failure("Unknown device / جهاز غير معروف");

        var hash = HashApiKey(apiKeyPlaintext);
        if (!FixedTimeEquals(device.ApiKeyHash, hash))
        {
            _logger.LogWarning("Biometric push rejected: invalid API key for device {DeviceId}", deviceId);
            return Result<BiometricPushIngestResultDto>.Failure("Invalid device credentials / بيانات اعتماد الجهاز غير صالحة");
        }

        if (!device.IsEnabled)
            return Result<BiometricPushIngestResultDto>.Failure("Device is disabled / الجهاز معطّل");

        if (device.IntegrationType != BiometricIntegrationTypes.PushToLocal
            && device.IntegrationType != BiometricIntegrationTypes.VendorMiddleware)
        {
            return Result<BiometricPushIngestResultDto>.Failure(
                "This device is not configured for push ingest / هذا الجهاز غير مضبوط لاستقبال الدفع");
        }

        // Anonymous push has no TenantMiddleware — bind ambient tenant from the authenticated device
        // so global query filters can see mappings/employees/attendance for this gym.
        if (!_tenantContext.IsInitialized || _tenantContext.TenantId != device.TenantId)
        {
            var tenant = await _db.Tenants.AsNoTracking().IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == device.TenantId && !t.IsDeleted);
            _tenantContext.SetTenant(
                device.TenantId,
                tenant?.Name ?? "Local",
                tenant?.TimeZone ?? "Africa/Cairo");
        }

        var events = request.Events ?? new List<BiometricPushEventRequest>();
        if (events.Count == 0)
            return Result<BiometricPushIngestResultDto>.Failure("No events in batch / لا توجد أحداث");
        if (events.Count > 200)
            return Result<BiometricPushIngestResultDto>.Failure("Batch too large (max 200) / الدفعة كبيرة جداً");

        var result = new BiometricPushIngestResultDto();
        var processed = new List<BiometricAttendanceEvent>();

        foreach (var item in events)
        {
            var ev = await PersistAndProcessAsync(device, item);
            processed.Add(ev);
            switch (ev.ProcessingStatus)
            {
                case BiometricEventStatuses.Applied: result.Applied++; result.Accepted++; break;
                case BiometricEventStatuses.Duplicate: result.Duplicates++; break;
                case BiometricEventStatuses.NeedsReview: result.NeedsReview++; result.Accepted++; break;
                case BiometricEventStatuses.Rejected: result.Rejected++; break;
                default: result.Accepted++; break;
            }
        }

        device.LastSuccessfulSyncAtUtc = DateTime.UtcNow;
        device.HealthStatus = BiometricDeviceHealthStatuses.Receiving;
        device.LastError = null;
        device.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        result.Items = await MapEventsAsync(device.TenantId, processed);
        return Result<BiometricPushIngestResultDto>.Success(result);
    }

    public async Task<Result<BiometricAttendanceEventDto>> ReprocessAsync(Guid tenantId, Guid eventId)
    {
        var ev = await _db.BiometricAttendanceEvents
            .FirstOrDefaultAsync(e => e.Id == eventId && e.TenantId == tenantId);
        if (ev == null)
            return Result<BiometricAttendanceEventDto>.Failure("Event not found / الحدث غير موجود");
        if (ev.ProcessingStatus is BiometricEventStatuses.Duplicate or BiometricEventStatuses.Applied)
            return Result<BiometricAttendanceEventDto>.Failure(
                "Applied/duplicate events are not reprocessed / لا يُعاد معالجة الأحداث المطبّقة أو المكررة");

        var device = await _db.BiometricDevices.FirstOrDefaultAsync(d => d.Id == ev.BiometricDeviceId && d.TenantId == tenantId);
        if (device == null)
            return Result<BiometricAttendanceEventDto>.Failure("Device not found / الجهاز غير موجود");

        await ReconcileAsync(device, ev);
        await _db.SaveChangesAsync();
        var mapped = await MapEventsAsync(tenantId, new List<BiometricAttendanceEvent> { ev });
        return Result<BiometricAttendanceEventDto>.Success(mapped[0]);
    }

    private async Task<BiometricAttendanceEvent> PersistAndProcessAsync(
        BiometricDevice device, BiometricPushEventRequest item)
    {
        var receivedAt = DateTime.UtcNow;
        var deviceUserId = (item.DeviceUserId ?? string.Empty).Trim();
        var vendorEventId = string.IsNullOrWhiteSpace(item.VendorEventId) ? null : item.VendorEventId.Trim();
        var direction = NormalizePunchDirection(item.PunchDirection);

        if (string.IsNullOrEmpty(deviceUserId))
        {
            var rejected = NewEvent(device, vendorEventId, deviceUserId: "?", DateTime.UtcNow, receivedAt, direction, item);
            rejected.ProcessingStatus = BiometricEventStatuses.Rejected;
            rejected.ReviewReason = "Missing device user id";
            rejected.ValidationResult = "MissingDeviceUserId";
            _db.BiometricAttendanceEvents.Add(rejected);
            await _db.SaveChangesAsync();
            return rejected;
        }

        if (!TryParseDeviceTimestamp(item.DeviceTimestamp, out var deviceUtc, out var originalText))
        {
            var rejected = NewEvent(device, vendorEventId, deviceUserId, DateTime.UtcNow, receivedAt, direction, item);
            rejected.OriginalDeviceTimeText = item.DeviceTimestamp;
            rejected.ProcessingStatus = BiometricEventStatuses.Rejected;
            rejected.ReviewReason = "Invalid device timestamp";
            rejected.ValidationResult = "InvalidTimestamp";
            _db.BiometricAttendanceEvents.Add(rejected);
            await _db.SaveChangesAsync();
            return rejected;
        }

        // Strong / fallback duplicate detection before insert.
        BiometricAttendanceEvent? existing = null;
        if (vendorEventId != null)
        {
            existing = await _db.BiometricAttendanceEvents
                .FirstOrDefaultAsync(e =>
                    e.TenantId == device.TenantId
                    && e.BiometricDeviceId == device.Id
                    && e.VendorEventId == vendorEventId);
        }
        else
        {
            existing = await _db.BiometricAttendanceEvents
                .FirstOrDefaultAsync(e =>
                    e.TenantId == device.TenantId
                    && e.BiometricDeviceId == device.Id
                    && e.VendorEventId == null
                    && e.DeviceUserId == deviceUserId
                    && e.DeviceTimestampUtc == deviceUtc);
        }

        if (existing != null)
        {
            existing.ProcessingStatus = BiometricEventStatuses.Duplicate;
            existing.ValidationResult ??= "Duplicate";
            existing.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return existing;
        }

        var ev = NewEvent(device, vendorEventId, deviceUserId, deviceUtc, receivedAt, direction, item);
        ev.OriginalDeviceTimeText = originalText;
        _db.BiometricAttendanceEvents.Add(ev);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Concurrent duplicate hit unique index — treat as duplicate.
            var dup = vendorEventId != null
                ? await _db.BiometricAttendanceEvents.FirstOrDefaultAsync(e =>
                    e.TenantId == device.TenantId && e.BiometricDeviceId == device.Id && e.VendorEventId == vendorEventId)
                : await _db.BiometricAttendanceEvents.FirstOrDefaultAsync(e =>
                    e.TenantId == device.TenantId && e.BiometricDeviceId == device.Id
                    && e.VendorEventId == null && e.DeviceUserId == deviceUserId && e.DeviceTimestampUtc == deviceUtc);
            if (dup != null)
            {
                dup.ProcessingStatus = BiometricEventStatuses.Duplicate;
                dup.ValidationResult = "Duplicate";
                await _db.SaveChangesAsync();
                return dup;
            }
            throw;
        }

        await ReconcileAsync(device, ev);
        await _db.SaveChangesAsync();
        return ev;
    }

    private async Task ReconcileAsync(BiometricDevice device, BiometricAttendanceEvent ev)
    {
        var skewSeconds = (int)Math.Round((ev.DeviceTimestampUtc - ev.ReceivedAtUtc).TotalSeconds);
        ev.ClockSkewSeconds = skewSeconds;
        var absSkewMinutes = Math.Abs(skewSeconds) / 60.0;
        var maxSkew = Math.Max(1, _options.ProvisionalMaxClockSkewMinutes);

        if (absSkewMinutes > maxSkew)
        {
            HoldForReview(ev, "ClockSkew",
                $"Device clock skew ~{absSkewMinutes:0} min exceeds provisional threshold ({maxSkew} min) — PO approval pending for production threshold.");
            return;
        }

        var maxFuture = TimeSpan.FromDays(Math.Max(1, _options.MaxFutureSkewDays));
        var maxPast = TimeSpan.FromDays(Math.Max(1, _options.MaxPastSkewDays));
        if (ev.DeviceTimestampUtc > ev.ReceivedAtUtc + maxFuture)
        {
            HoldForReview(ev, "TimestampImplausibleFuture", "Device timestamp too far in the future.");
            return;
        }
        if (ev.DeviceTimestampUtc < ev.ReceivedAtUtc - maxPast)
        {
            HoldForReview(ev, "TimestampTooOld",
                "Device timestamp older than auto-apply window — raw event preserved for review/backlog.");
            return;
        }

        var mapping = await _db.BiometricEmployeeMappings
            .FirstOrDefaultAsync(m =>
                m.TenantId == device.TenantId
                && m.BiometricDeviceId == device.Id
                && m.DeviceUserId == ev.DeviceUserId
                && m.IsEnabled);

        if (mapping == null)
        {
            HoldForReview(ev, "UnmappedDeviceUser", "No enabled employee mapping for this device user id.");
            return;
        }

        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == mapping.EmployeeId && e.TenantId == device.TenantId);
        if (employee == null)
        {
            HoldForReview(ev, "EmployeeMissing", "Mapped employee no longer exists.");
            return;
        }

        ev.ResolvedEmployeeId = employee.Id;

        if (!string.Equals(employee.Status, EmployeeStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            HoldForReview(ev, "EmployeeIneligible",
                $"Employee status is {employee.Status} — automatic attribution blocked.");
            return;
        }

        // Accepted time for attendance math = validated device timestamp (not silently rewritten).
        ev.AcceptedTimestampUtc = DateTime.SpecifyKind(ev.DeviceTimestampUtc, DateTimeKind.Utc);

        var attendanceDate = await ResolveAttendanceDateAsync(device.TenantId, employee.Id, ev.AcceptedTimestampUtc.Value);
        var row = await _db.EmployeeAttendances
            .FirstOrDefaultAsync(a =>
                a.TenantId == device.TenantId
                && a.EmployeeId == employee.Id
                && a.AttendanceDate == attendanceDate);

        if (row?.LeaveRequestId != null && row.CheckInAtUtc == null
            && string.Equals(row.Status, AttendanceStatuses.OnLeave, StringComparison.OrdinalIgnoreCase))
        {
            HoldForReview(ev, "OnLeaveConflict", "Employee has an OnLeave attendance placeholder for this date.");
            return;
        }

        var schedule = await _db.EmployeeScheduleAssignments.AsNoTracking()
            .Include(a => a.EmployeeShift)
            .FirstOrDefaultAsync(a =>
                a.TenantId == device.TenantId
                && a.EmployeeId == employee.Id
                && a.Date == attendanceDate);

        // Explicit CheckOut without prior check-in → review (do not invent check-in).
        if (string.Equals(ev.PunchDirection, BiometricPunchDirections.CheckOut, StringComparison.OrdinalIgnoreCase)
            && (row == null || row.CheckInAtUtc == null))
        {
            HoldForReview(ev, "CheckoutWithoutCheckin", "Device reported check-out but no check-in exists.");
            return;
        }

        if (row == null)
        {
            row = new EmployeeAttendance
            {
                TenantId = device.TenantId,
                EmployeeId = employee.Id,
                AttendanceDate = attendanceDate,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.EmployeeAttendances.Add(row);
        }

        // First valid punch → check-in (when empty).
        if (row.CheckInAtUtc == null)
        {
            if (string.Equals(ev.PunchDirection, BiometricPunchDirections.CheckOut, StringComparison.OrdinalIgnoreCase))
            {
                HoldForReview(ev, "CheckoutWithoutCheckin", "Device reported check-out but no check-in exists.");
                return;
            }

            ApplyCheckIn(row, schedule, ev.AcceptedTimestampUtc.Value);
            ev.ProcessingStatus = BiometricEventStatuses.Applied;
            ev.ValidationResult = "AppliedCheckIn";
            ev.ResultingAttendanceId = row.Id;
            ev.ReviewReason = null;
            ev.UpdatedAtUtc = DateTime.UtcNow;
            await _audit.LogAsync("biometric_event.applied_checkin", "BiometricAttendanceEvent", ev.Id, null,
                new { ev.ResolvedEmployeeId, row.AttendanceDate, row.CheckInAtUtc, row.Source });
            return;
        }

        // Existing check-in from another source — do not silently rewrite earlier/later check-in.
        var existingSource = row.Source ?? AttendanceSources.Manual;
        var isDeviceOwned = string.Equals(existingSource, AttendanceSources.Device, StringComparison.OrdinalIgnoreCase);

        if (ev.AcceptedTimestampUtc.Value < row.CheckInAtUtc.Value)
        {
            if (!isDeviceOwned)
            {
                HoldForReview(ev, "EarlierThanExistingCheckIn",
                    $"Earlier device punch than existing {existingSource} check-in — preserved for review.");
                return;
            }

            // Device-owned row: allow first-punch correction to an earlier device punch (still audited).
            var before = row.CheckInAtUtc;
            ApplyCheckIn(row, schedule, ev.AcceptedTimestampUtc.Value);
            // Keep checkout if still valid
            if (row.CheckOutAtUtc != null && row.CheckOutAtUtc <= row.CheckInAtUtc)
            {
                HoldForReview(ev, "CheckoutInvalidAfterCheckInMove",
                    "Moving check-in would invalidate check-out — held for review.");
                row.CheckInAtUtc = before;
                return;
            }
            if (row.CheckOutAtUtc != null)
                RecalcCheckout(row, schedule);

            ev.ProcessingStatus = BiometricEventStatuses.Applied;
            ev.ValidationResult = "AppliedEarlierCheckIn";
            ev.ResultingAttendanceId = row.Id;
            ev.UpdatedAtUtc = DateTime.UtcNow;
            await _audit.LogAsync("biometric_event.applied_earlier_checkin", "BiometricAttendanceEvent", ev.Id,
                new { before }, new { row.CheckInAtUtc });
            return;
        }

        // Same timestamp as check-in → duplicate punch, ignore for attendance mutation.
        if (row.CheckInAtUtc.HasValue
            && Math.Abs((ev.AcceptedTimestampUtc.Value - row.CheckInAtUtc.Value).TotalSeconds) < 1)
        {
            ev.ProcessingStatus = BiometricEventStatuses.Duplicate;
            ev.ValidationResult = "SameAsCheckIn";
            ev.ResultingAttendanceId = row.Id;
            ev.UpdatedAtUtc = DateTime.UtcNow;
            return;
        }

        // Explicit CheckIn when already checked in → review (do not invent second day row).
        if (string.Equals(ev.PunchDirection, BiometricPunchDirections.CheckIn, StringComparison.OrdinalIgnoreCase)
            && row.CheckInAtUtc != null)
        {
            HoldForReview(ev, "ExtraCheckInDirection",
                "Device marked CheckIn but attendance already has check-in — held for review.");
            return;
        }

        // Last valid punch → check-out (update if later). Single-punch days stay without checkout.
        if (row.CheckOutAtUtc != null && ev.AcceptedTimestampUtc.Value <= row.CheckOutAtUtc.Value)
        {
            // Intermediate punch between in and out — do not shrink checkout; leave as review only if direction says CheckOut earlier.
            if (string.Equals(ev.PunchDirection, BiometricPunchDirections.CheckOut, StringComparison.OrdinalIgnoreCase)
                && ev.AcceptedTimestampUtc.Value < row.CheckOutAtUtc.Value)
            {
                HoldForReview(ev, "EarlierCheckoutConflict",
                    "Earlier check-out punch than existing check-out — held for review.");
                return;
            }

            ev.ProcessingStatus = BiometricEventStatuses.Applied;
            ev.ValidationResult = "IgnoredIntermediatePunch";
            ev.ResultingAttendanceId = row.Id;
            ev.ReviewReason = null;
            ev.UpdatedAtUtc = DateTime.UtcNow;
            return;
        }

        var accepted = ev.AcceptedTimestampUtc!.Value;
        var checkIn = row.CheckInAtUtc!.Value;
        if (accepted <= checkIn)
        {
            HoldForReview(ev, "NotAfterCheckIn", "Punch is not after check-in.");
            return;
        }

        row.CheckOutAtUtc = accepted;
        RecalcCheckout(row, schedule);
        row.UpdatedAtUtc = DateTime.UtcNow;

        // Source stays as original first source for attribution of the day row; device contribution is on the event.
        ev.ProcessingStatus = BiometricEventStatuses.Applied;
        ev.ValidationResult = row.CheckOutAtUtc == ev.AcceptedTimestampUtc ? "AppliedCheckOut" : "AppliedCheckOutUpdate";
        ev.ResultingAttendanceId = row.Id;
        ev.ReviewReason = null;
        ev.UpdatedAtUtc = DateTime.UtcNow;
        await _audit.LogAsync("biometric_event.applied_checkout", "BiometricAttendanceEvent", ev.Id, null,
            new { ev.ResolvedEmployeeId, row.AttendanceDate, row.CheckOutAtUtc, row.WorkedMinutes, row.OvertimeMinutes, row.Source });
    }

    private static void ApplyCheckIn(EmployeeAttendance row, EmployeeScheduleAssignment? schedule, DateTime checkInUtc)
    {
        row.ScheduleId = schedule?.Id;
        row.CheckInAtUtc = checkInUtc;
        var (late, status) = AttendanceCalculator.ComputeCheckIn(
            checkInUtc, row.AttendanceDate, schedule?.EmployeeShift?.StartTime, schedule?.EmployeeShift?.GraceMinutes ?? 0);
        row.LateMinutes = late;
        row.Status = status;
        row.Source = AttendanceSources.Device;
        row.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static void RecalcCheckout(EmployeeAttendance row, EmployeeScheduleAssignment? schedule)
    {
        if (row.CheckInAtUtc == null || row.CheckOutAtUtc == null)
            return;
        var calc = AttendanceCalculator.ComputeCheckOut(
            row.CheckInAtUtc.Value,
            row.CheckOutAtUtc.Value,
            row.AttendanceDate,
            schedule?.EmployeeShift?.StartTime,
            schedule?.EmployeeShift?.EndTime);
        if (calc.IsSuccess)
        {
            row.WorkedMinutes = calc.Data.WorkedMinutes;
            row.OvertimeMinutes = calc.Data.OvertimeMinutes;
        }
    }

    /// <summary>
    /// Resolves the attendance calendar date for a punch. Uses Cairo date of the punch, then
    /// attributes to the previous day when an overnight schedule covers the punch.
    /// </summary>
    private async Task<DateOnly> ResolveAttendanceDateAsync(Guid tenantId, Guid employeeId, DateTime punchUtc)
    {
        var cairoDate = MembershipOperational.ToCairoDate(punchUtc);
        var todaySchedule = await _db.EmployeeScheduleAssignments.AsNoTracking()
            .Include(a => a.EmployeeShift)
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.EmployeeId == employeeId && a.Date == cairoDate);

        if (todaySchedule?.EmployeeShift != null)
        {
            var shift = todaySchedule.EmployeeShift;
            if (shift.EndTime > shift.StartTime)
                return cairoDate;
            // Overnight starting today — punch on start calendar day or next morning still belongs here if before end.
            var endUtc = AttendanceCalculator.ComputeShiftEndUtc(cairoDate, shift.StartTime, shift.EndTime);
            if (punchUtc <= endUtc.AddHours(2)) // small buffer past scheduled end still same attendance day
                return cairoDate;
        }

        var prevDate = cairoDate.AddDays(-1);
        var prevSchedule = await _db.EmployeeScheduleAssignments.AsNoTracking()
            .Include(a => a.EmployeeShift)
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.EmployeeId == employeeId && a.Date == prevDate);
        if (prevSchedule?.EmployeeShift != null)
        {
            var shift = prevSchedule.EmployeeShift;
            if (shift.EndTime <= shift.StartTime)
            {
                var endUtc = AttendanceCalculator.ComputeShiftEndUtc(prevDate, shift.StartTime, shift.EndTime);
                var startUtc = AttendanceCalculator.ComputeShiftStartUtc(prevDate, shift.StartTime);
                if (punchUtc >= startUtc && punchUtc <= endUtc.AddHours(2))
                    return prevDate;
            }
        }

        return cairoDate;
    }

    private static void HoldForReview(BiometricAttendanceEvent ev, string validation, string reason)
    {
        ev.ProcessingStatus = BiometricEventStatuses.NeedsReview;
        ev.ValidationResult = validation;
        ev.ReviewReason = reason;
        ev.AcceptedTimestampUtc = null;
        ev.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static BiometricAttendanceEvent NewEvent(
        BiometricDevice device, string? vendorEventId, string deviceUserId,
        DateTime deviceUtc, DateTime receivedAt, string direction, BiometricPushEventRequest item)
        => new()
        {
            TenantId = device.TenantId,
            BiometricDeviceId = device.Id,
            VendorEventId = vendorEventId,
            DeviceUserId = deviceUserId,
            DeviceTimestampUtc = DateTime.SpecifyKind(deviceUtc, DateTimeKind.Utc),
            ReceivedAtUtc = receivedAt,
            PunchDirection = direction,
            ProcessingStatus = BiometricEventStatuses.Pending,
            SafePayloadJson = Normalize(item.SafePayloadJson, 4000),
            CreatedAtUtc = DateTime.UtcNow
        };

    private static bool TryParseDeviceTimestamp(string? text, out DateTime utc, out string? original)
    {
        utc = default;
        original = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (original == null)
            return false;

        // Prefer DateTimeOffset so Z / +02:00 are preserved as absolute instants.
        if (DateTimeOffset.TryParse(original, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dto))
        {
            utc = dto.UtcDateTime;
            return true;
        }

        if (!DateTime.TryParse(original, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return false;

        if (dt.Kind == DateTimeKind.Utc)
        {
            utc = dt;
            return true;
        }

        // Unspecified / local wall-clock is treated as Cairo wall-clock (gym convention).
        var cairo = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
        var unspecified = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, cairo);
        return true;
    }

    private static string NormalizePunchDirection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return BiometricPunchDirections.Unknown;
        var v = value.Trim();
        if (BiometricPunchDirections.All.Contains(v))
            return BiometricPunchDirections.All.First(x => x.Equals(v, StringComparison.OrdinalIgnoreCase));
        return BiometricPunchDirections.Unknown;
    }

    private static (string Plaintext, string Hash, string Prefix) GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var plaintext = "hmbio_" + Convert.ToHexString(bytes).ToLowerInvariant();
        var hash = HashApiKey(plaintext);
        var prefix = plaintext.Length <= 14 ? plaintext : plaintext[..14];
        return (plaintext, hash, prefix);
    }

    internal static string HashApiKey(string plaintext)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext.Trim()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a ?? string.Empty);
        var bb = Encoding.UTF8.GetBytes(b ?? string.Empty);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static string? Normalize(string? value, int max = 120)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var t = value.Trim();
        return t.Length <= max ? t : t[..max];
    }

    private static BiometricDeviceDto MapDevice(BiometricDevice d, int mappedCount) => new()
    {
        Id = d.Id,
        DeviceCode = d.DeviceCode,
        DisplayName = d.DisplayName,
        Vendor = d.Vendor,
        Model = d.Model,
        IntegrationType = d.IntegrationType,
        LocationLabel = d.LocationLabel,
        IsEnabled = d.IsEnabled,
        HealthStatus = d.HealthStatus,
        LastSuccessfulSyncAtUtc = d.LastSuccessfulSyncAtUtc,
        LastError = d.LastError,
        SafeConfigJson = d.SafeConfigJson,
        ApiKeyPrefix = d.ApiKeyPrefix,
        ApiKeyRotatedAtUtc = d.ApiKeyRotatedAtUtc,
        MappedEmployeeCount = mappedCount,
        CreatedAtUtc = d.CreatedAtUtc
    };

    private static BiometricDeviceCreatedDto MapDeviceCreated(BiometricDevice d, int mappedCount, string plaintext)
    {
        var dto = MapDevice(d, mappedCount);
        return new BiometricDeviceCreatedDto
        {
            Id = dto.Id,
            DeviceCode = dto.DeviceCode,
            DisplayName = dto.DisplayName,
            Vendor = dto.Vendor,
            Model = dto.Model,
            IntegrationType = dto.IntegrationType,
            LocationLabel = dto.LocationLabel,
            IsEnabled = dto.IsEnabled,
            HealthStatus = dto.HealthStatus,
            LastSuccessfulSyncAtUtc = dto.LastSuccessfulSyncAtUtc,
            LastError = dto.LastError,
            SafeConfigJson = dto.SafeConfigJson,
            ApiKeyPrefix = dto.ApiKeyPrefix,
            ApiKeyRotatedAtUtc = dto.ApiKeyRotatedAtUtc,
            MappedEmployeeCount = dto.MappedEmployeeCount,
            CreatedAtUtc = dto.CreatedAtUtc,
            ApiKeyPlaintext = plaintext
        };
    }

    private async Task<List<BiometricEmployeeMappingDto>> MapMappingsAsync(Guid tenantId, List<BiometricEmployeeMapping> rows)
    {
        var deviceIds = rows.Select(r => r.BiometricDeviceId).Distinct().ToList();
        var employeeIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var devices = await _db.BiometricDevices.AsNoTracking()
            .Where(d => d.TenantId == tenantId && deviceIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id);
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        return rows.Select(m =>
        {
            devices.TryGetValue(m.BiometricDeviceId, out var d);
            employees.TryGetValue(m.EmployeeId, out var e);
            return new BiometricEmployeeMappingDto
            {
                Id = m.Id,
                BiometricDeviceId = m.BiometricDeviceId,
                DeviceName = d?.DisplayName ?? string.Empty,
                EmployeeId = m.EmployeeId,
                EmployeeName = e != null ? $"{e.FirstName} {e.LastName}" : string.Empty,
                EmployeeNumber = e?.EmployeeNumber ?? string.Empty,
                EmployeeStatus = e?.Status ?? string.Empty,
                DeviceUserId = m.DeviceUserId,
                IsEnabled = m.IsEnabled,
                DisabledReason = m.DisabledReason,
                DisabledAtUtc = m.DisabledAtUtc,
                RemoteDisableStatus = m.RemoteDisableStatus,
                RemoteDisableNote = m.RemoteDisableNote
            };
        }).ToList();
    }

    private async Task<List<BiometricAttendanceEventDto>> MapEventsAsync(Guid tenantId, List<BiometricAttendanceEvent> rows)
    {
        var deviceIds = rows.Select(r => r.BiometricDeviceId).Distinct().ToList();
        var employeeIds = rows.Where(r => r.ResolvedEmployeeId.HasValue).Select(r => r.ResolvedEmployeeId!.Value).Distinct().ToList();
        var devices = await _db.BiometricDevices.AsNoTracking()
            .Where(d => d.TenantId == tenantId && deviceIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id);
        var employees = employeeIds.Count == 0
            ? new Dictionary<Guid, Employee>()
            : await _db.Employees.AsNoTracking()
                .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id);

        return rows.Select(e =>
        {
            devices.TryGetValue(e.BiometricDeviceId, out var d);
            Employee? emp = null;
            if (e.ResolvedEmployeeId.HasValue)
                employees.TryGetValue(e.ResolvedEmployeeId.Value, out emp);
            return new BiometricAttendanceEventDto
            {
                Id = e.Id,
                BiometricDeviceId = e.BiometricDeviceId,
                DeviceName = d?.DisplayName ?? string.Empty,
                VendorEventId = e.VendorEventId,
                DeviceUserId = e.DeviceUserId,
                DeviceTimestampUtc = e.DeviceTimestampUtc,
                OriginalDeviceTimeText = e.OriginalDeviceTimeText,
                ReceivedAtUtc = e.ReceivedAtUtc,
                AcceptedTimestampUtc = e.AcceptedTimestampUtc,
                PunchDirection = e.PunchDirection,
                ProcessingStatus = e.ProcessingStatus,
                ReviewReason = e.ReviewReason,
                ValidationResult = e.ValidationResult,
                ResolvedEmployeeId = e.ResolvedEmployeeId,
                ResolvedEmployeeName = emp != null ? $"{emp.FirstName} {emp.LastName}" : null,
                ResultingAttendanceId = e.ResultingAttendanceId,
                ClockSkewSeconds = e.ClockSkewSeconds,
                SafePayloadJson = e.SafePayloadJson
            };
        }).ToList();
    }
}

/// <summary>
/// Test-only / preview adapter — normalizes a JSON array of push events. Never connects to hardware.
/// </summary>
public sealed class MockBiometricDeviceAdapter : IBiometricDeviceAdapter
{
    public string AdapterKey => "mock";
    public bool SupportsRemoteUserDisable => false;

    public IReadOnlyList<BiometricPushEventRequest> NormalizeEvents(string rawPayloadJson)
    {
        if (string.IsNullOrWhiteSpace(rawPayloadJson))
            return Array.Empty<BiometricPushEventRequest>();
        try
        {
            var batch = System.Text.Json.JsonSerializer.Deserialize<BiometricPushBatchRequest>(rawPayloadJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return (IReadOnlyList<BiometricPushEventRequest>)(batch?.Events ?? new List<BiometricPushEventRequest>());
        }
        catch
        {
            return Array.Empty<BiometricPushEventRequest>();
        }
    }

    public Task<Result<bool>> TryDisableRemoteUserAsync(Guid tenantId, Guid deviceId, string deviceUserId, CancellationToken cancellationToken = default)
        => Task.FromResult(Result<bool>.Failure("Remote disable not supported by mock adapter / التعطيل عن بُعد غير مدعوم"));
}
