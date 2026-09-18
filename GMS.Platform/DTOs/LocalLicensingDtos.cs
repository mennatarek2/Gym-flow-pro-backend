namespace GMS.Platform.DTOs;

using GMS.Core.Licensing;

public class ActivationResultDto
{
    public bool Success { get; set; }

    /// <summary>See LocalActivationResults - always set, success or not, for logging/UI.</summary>
    public string Result { get; set; } = string.Empty;

    public string? Message { get; set; }

    /// <summary>Only populated when Success is true.</summary>
    public LocalLicensePayload? License { get; set; }
}

public class LocalLicenseListItemDto
{
    public Guid Id { get; set; }
    public string LicenseKey { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public Guid? ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Edition { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int DeviceLimit { get; set; }
    public int ActiveInstallationCount { get; set; }
    public DateTime? IssuedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Newest LastValidatedAtUtc among currently active installations. Null when none are active.</summary>
    public DateTime? LastValidatedAtUtc { get; set; }
    /// <summary>Status of the currently active installation used for LastValidatedAtUtc. Null when none are active.</summary>
    public string? InstallationStatus { get; set; }
    /// <summary>Gym code reported by the current (or last known) installation. Control-plane identity, not Local business data.</summary>
    public string? GymCode { get; set; }
    /// <summary>Gym name reported by the current (or last known) installation.</summary>
    public string? GymName { get; set; }
    /// <summary>Application version reported by the current (or last known) installation.</summary>
    public string? AppVersion { get; set; }
}

public class LocalLicenseDetailDto : LocalLicenseListItemDto
{
    public string? CustomerContact { get; set; }
    public string? DealReference { get; set; }
    public string? Notes { get; set; }
    public string? RevokedReason { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public List<LocalInstallationDto> Installations { get; set; } = new();
    public List<LocalLicenseChangeDto> RecentChanges { get; set; } = new();
    public int TransferCount { get; set; }
    public int SuspiciousEventCount { get; set; }
    public List<LocalLifecycleEventDto> RecentOperations { get; set; } = new();
}

public class LocalInstallationDto
{
    public Guid Id { get; set; }
    public string InstallationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime FirstActivatedAtUtc { get; set; }
    public DateTime? LastValidatedAtUtc { get; set; }
    public DateTime? DeactivatedAtUtc { get; set; }
    public string? DeactivationReason { get; set; }
    public string? GymCode { get; set; }
    public string? GymName { get; set; }
    public string? AppVersion { get; set; }
}

public class LocalLicenseChangeDto
{
    public string ChangeType { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string InitiatedBy { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class LocalLifecycleEventDto
{
    public Guid OperationId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? InstallationId { get; set; }
    public string? GymCode { get; set; }
    public string? GymName { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class RecordLocalLifecycleEventRequest
{
    public string LicenseKey { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
    public Guid OperationId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? GymCode { get; set; }
    public string? GymName { get; set; }
    public string? Message { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? AppVersion { get; set; }
}

public class IssueLocalLicenseRequest
{
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerContact { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? ContractId { get; set; }
    public string? DealReference { get; set; }
    public string Edition { get; set; } = "Lifetime";
    public int DeviceLimit { get; set; } = 1;
    public string? Notes { get; set; }
}
