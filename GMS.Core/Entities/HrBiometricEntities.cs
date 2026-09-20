namespace GMS.Core.Entities;

using GMS.Core.Constants;

/// <summary>
/// Registered biometric attendance terminal (or vendor bridge) for a gym.
/// Push-to-Local foundation — does not store fingerprint templates or plaintext device secrets.
/// </summary>
public class BiometricDevice : BaseEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Stable external code for ops (tenant-scoped), e.g. BIO-FRONT-01.</summary>
    public string DeviceCode { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Vendor label only — not a verified compatibility claim.</summary>
    public string? Vendor { get; set; }

    /// <summary>Model label only — not a verified compatibility claim.</summary>
    public string? Model { get; set; }

    /// <summary>PushToLocal | LanPull | VendorMiddleware — see <see cref="BiometricIntegrationTypes"/>.</summary>
    public string IntegrationType { get; set; } = BiometricIntegrationTypes.PushToLocal;

    public string? LocationLabel { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>AwaitingEvents | Receiving | Disabled | Error — honest status; never implies unverified TCP.</summary>
    public string HealthStatus { get; set; } = BiometricDeviceHealthStatuses.AwaitingEvents;

    public DateTime? LastSuccessfulSyncAtUtc { get; set; }
    public string? LastError { get; set; }

    /// <summary>Non-secret configuration JSON (host hint, port hint, notes). Never passwords.</summary>
    public string? SafeConfigJson { get; set; }

    /// <summary>SHA-256 hex of the device push API key. Plaintext key is shown once at creation/rotation.</summary>
    public string ApiKeyHash { get; set; } = string.Empty;

    /// <summary>First 8 chars of the key for support identification — not sufficient to authenticate.</summary>
    public string ApiKeyPrefix { get; set; } = string.Empty;

    public DateTime? ApiKeyRotatedAtUtc { get; set; }

    public Guid? CreatedByAppUserId { get; set; }
    public Guid? UpdatedByAppUserId { get; set; }

    public Tenant? Tenant { get; set; }
    public ICollection<BiometricEmployeeMapping> Mappings { get; set; } = new List<BiometricEmployeeMapping>();
    public ICollection<BiometricAttendanceEvent> Events { get; set; } = new List<BiometricAttendanceEvent>();
}

/// <summary>
/// Explicit mapping between a HyMotion <see cref="Employee"/> and a device-local user id.
/// Device user id is never assumed equal to Employee.Id.
/// </summary>
public class BiometricEmployeeMapping : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid BiometricDeviceId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>User number / PIN on the physical device or vendor bridge.</summary>
    public string DeviceUserId { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Why the mapping was disabled (HR status, manual, reassignment).</summary>
    public string? DisabledReason { get; set; }
    public DateTime? DisabledAtUtc { get; set; }

    /// <summary>
    /// Pending | Confirmed | Failed | NotSupported — remote device user disablement is tracked
    /// separately from local mapping state. Never claim Confirmed without a verified adapter.
    /// </summary>
    public string RemoteDisableStatus { get; set; } = BiometricRemoteDisableStatuses.NotSupported;
    public DateTime? RemoteDisableRequestedAtUtc { get; set; }
    public DateTime? RemoteDisableConfirmedAtUtc { get; set; }
    public string? RemoteDisableNote { get; set; }

    public BiometricDevice? BiometricDevice { get; set; }
    public Employee? Employee { get; set; }
}

/// <summary>
/// Durable raw biometric attendance evidence. Fingerprint templates are never stored.
/// Processing updates status fields; the original payload timestamps are preserved.
/// </summary>
public class BiometricAttendanceEvent : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid BiometricDeviceId { get; set; }

    /// <summary>Vendor/device event id when supplied. Used for strong idempotency.</summary>
    public string? VendorEventId { get; set; }

    public string DeviceUserId { get; set; } = string.Empty;

    /// <summary>Timestamp as reported by the device (UTC normalized for storage; original offset kept in OriginalDeviceTimeText when needed).</summary>
    public DateTime DeviceTimestampUtc { get; set; }

    /// <summary>Optional original device time string as received (never silently rewritten).</summary>
    public string? OriginalDeviceTimeText { get; set; }

    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Accepted timestamp used for attendance math when auto-applied; null when held for review.</summary>
    public DateTime? AcceptedTimestampUtc { get; set; }

    /// <summary>Unknown | CheckIn | CheckOut — see <see cref="BiometricPunchDirections"/>.</summary>
    public string PunchDirection { get; set; } = BiometricPunchDirections.Unknown;

    /// <summary>Pending | Applied | Duplicate | NeedsReview | Rejected — see <see cref="BiometricEventStatuses"/>.</summary>
    public string ProcessingStatus { get; set; } = BiometricEventStatuses.Pending;

    public string? ReviewReason { get; set; }
    public string? ValidationResult { get; set; }

    public Guid? ResolvedEmployeeId { get; set; }
    public Guid? ResultingAttendanceId { get; set; }

    public int? ClockSkewSeconds { get; set; }

    /// <summary>Optional opaque non-biometric metadata from the bridge (verify mode label, door id). No templates.</summary>
    public string? SafePayloadJson { get; set; }

    public BiometricDevice? BiometricDevice { get; set; }
    public Employee? ResolvedEmployee { get; set; }
    public EmployeeAttendance? ResultingAttendance { get; set; }
}
