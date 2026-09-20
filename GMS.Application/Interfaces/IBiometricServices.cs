namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Hr;

public interface IBiometricDeviceService
{
    Task<Result<List<BiometricDeviceDto>>> ListAsync(Guid tenantId);
    Task<Result<BiometricDeviceDto>> GetAsync(Guid tenantId, Guid deviceId);
    Task<Result<BiometricDeviceCreatedDto>> CreateAsync(Guid tenantId, CreateBiometricDeviceRequest request, Guid? actorAppUserId);
    Task<Result<BiometricDeviceDto>> UpdateAsync(Guid tenantId, Guid deviceId, UpdateBiometricDeviceRequest request, Guid? actorAppUserId);
    Task<Result<BiometricDeviceCreatedDto>> RotateApiKeyAsync(Guid tenantId, Guid deviceId, Guid? actorAppUserId);
}

public interface IBiometricMappingService
{
    Task<Result<List<BiometricEmployeeMappingDto>>> ListAsync(Guid tenantId, Guid? deviceId = null, Guid? employeeId = null, bool? enabledOnly = null);
    Task<Result<BiometricEmployeeMappingDto>> UpsertAsync(Guid tenantId, UpsertBiometricMappingRequest request);
    Task<Result<BiometricEmployeeMappingDto>> DisableAsync(Guid tenantId, Guid mappingId, string reason);
    /// <summary>Local mapping disablement when an employee becomes Suspended/Terminated. Does not claim remote device disablement.</summary>
    Task DisableMappingsForEmployeeAsync(Guid tenantId, Guid employeeId, string reason);
}

public interface IBiometricEventService
{
    Task<Result<List<BiometricAttendanceEventDto>>> ListAsync(
        Guid tenantId, DateTime? fromUtc = null, DateTime? toUtc = null,
        Guid? deviceId = null, Guid? employeeId = null, string? status = null);

    Task<Result<BiometricAttendanceEventDto>> GetEventAsync(Guid tenantId, Guid eventId);

    /// <summary>Authenticated push ingest for a previously registered device. Idempotent.</summary>
    Task<Result<BiometricPushIngestResultDto>> IngestPushAsync(Guid deviceId, string apiKeyPlaintext, BiometricPushBatchRequest request);

    /// <summary>Re-run reconciliation for a NeedsReview/Pending event (staff-triggered).</summary>
    Task<Result<BiometricAttendanceEventDto>> ReprocessAsync(Guid tenantId, Guid eventId);
}

/// <summary>
/// Optional future vendor adapter contract. Required capabilities are normalization helpers only.
/// Hardware connect / remote disable / backlog sync are optional and must not be claimed without a verified model.
/// </summary>
public interface IBiometricDeviceAdapter
{
    string AdapterKey { get; }

    /// <summary>Required: normalize a bridge payload into push event requests. No network I/O.</summary>
    IReadOnlyList<BiometricPushEventRequest> NormalizeEvents(string rawPayloadJson);

    /// <summary>Optional: true when this adapter can request remote user disablement on a verified device.</summary>
    bool SupportsRemoteUserDisable { get; }

    /// <summary>Optional: not implemented in foundation — throws or returns NotSupported.</summary>
    Task<Result<bool>> TryDisableRemoteUserAsync(Guid tenantId, Guid deviceId, string deviceUserId, CancellationToken cancellationToken = default);
}
