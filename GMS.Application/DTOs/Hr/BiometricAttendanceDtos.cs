namespace GMS.Application.DTOs.Hr;

/// <summary>Safe device list/detail DTO — never includes ApiKeyHash or plaintext secrets.</summary>
public class BiometricDeviceDto
{
    public Guid Id { get; set; }
    public string DeviceCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Vendor { get; set; }
    public string? Model { get; set; }
    public string IntegrationType { get; set; } = string.Empty;
    public string? LocationLabel { get; set; }
    public bool IsEnabled { get; set; }
    public string HealthStatus { get; set; } = string.Empty;
    public DateTime? LastSuccessfulSyncAtUtc { get; set; }
    public string? LastError { get; set; }
    public string? SafeConfigJson { get; set; }
    public string ApiKeyPrefix { get; set; } = string.Empty;
    public DateTime? ApiKeyRotatedAtUtc { get; set; }
    public int MappedEmployeeCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Returned only on create/rotate — plaintext key shown once.</summary>
public class BiometricDeviceCreatedDto : BiometricDeviceDto
{
    /// <summary>Plaintext push API key. Store securely; HyMotion only keeps a hash.</summary>
    public string ApiKeyPlaintext { get; set; } = string.Empty;
}

public class CreateBiometricDeviceRequest
{
    public string DeviceCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Vendor { get; set; }
    public string? Model { get; set; }
    public string IntegrationType { get; set; } = "PushToLocal";
    public string? LocationLabel { get; set; }
    public string? SafeConfigJson { get; set; }
}

public class UpdateBiometricDeviceRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? Vendor { get; set; }
    public string? Model { get; set; }
    public string IntegrationType { get; set; } = "PushToLocal";
    public string? LocationLabel { get; set; }
    public string? SafeConfigJson { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public class BiometricEmployeeMappingDto
{
    public Guid Id { get; set; }
    public Guid BiometricDeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public string EmployeeStatus { get; set; } = string.Empty;
    public string DeviceUserId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string? DisabledReason { get; set; }
    public DateTime? DisabledAtUtc { get; set; }
    public string RemoteDisableStatus { get; set; } = string.Empty;
    public string? RemoteDisableNote { get; set; }
}

public class UpsertBiometricMappingRequest
{
    public Guid BiometricDeviceId { get; set; }
    public Guid EmployeeId { get; set; }
    public string DeviceUserId { get; set; } = string.Empty;
}

public class BiometricAttendanceEventDto
{
    public Guid Id { get; set; }
    public Guid BiometricDeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string? VendorEventId { get; set; }
    public string DeviceUserId { get; set; } = string.Empty;
    public DateTime DeviceTimestampUtc { get; set; }
    public string? OriginalDeviceTimeText { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? AcceptedTimestampUtc { get; set; }
    public string PunchDirection { get; set; } = string.Empty;
    public string ProcessingStatus { get; set; } = string.Empty;
    public string? ReviewReason { get; set; }
    public string? ValidationResult { get; set; }
    public Guid? ResolvedEmployeeId { get; set; }
    public string? ResolvedEmployeeName { get; set; }
    public Guid? ResultingAttendanceId { get; set; }
    public int? ClockSkewSeconds { get; set; }
    public string? SafePayloadJson { get; set; }
}

public class BiometricPushEventRequest
{
    public string? VendorEventId { get; set; }
    public string DeviceUserId { get; set; } = string.Empty;
    /// <summary>ISO-8601 device time. Prefer offset/Z; stored as UTC + original text.</summary>
    public string DeviceTimestamp { get; set; } = string.Empty;
    public string? PunchDirection { get; set; }
    public string? SafePayloadJson { get; set; }
}

public class BiometricPushBatchRequest
{
    public List<BiometricPushEventRequest> Events { get; set; } = new();
}

public class BiometricPushIngestResultDto
{
    public int Accepted { get; set; }
    public int Duplicates { get; set; }
    public int NeedsReview { get; set; }
    public int Applied { get; set; }
    public int Rejected { get; set; }
    public List<BiometricAttendanceEventDto> Items { get; set; } = new();
}
