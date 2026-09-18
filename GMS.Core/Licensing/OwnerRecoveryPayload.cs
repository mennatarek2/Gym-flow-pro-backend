namespace GMS.Core.Licensing;

/// <summary>
/// Signed one-time authorization for Local Owner password recovery. Separate from
/// <see cref="LocalLicensePayload"/> — recovery must never be mistaken for a license grant
/// or device transfer. The Platform signs this; Local verifies with the embedded public key.
/// </summary>
public sealed class OwnerRecoveryPayload
{
    public const string PurposeValue = "owner-recovery-approval";

    public string Purpose { get; set; } = PurposeValue;
    public Guid RequestId { get; set; }
    public string InstallationId { get; set; } = string.Empty;
    public string GymCode { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public Guid Jti { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
