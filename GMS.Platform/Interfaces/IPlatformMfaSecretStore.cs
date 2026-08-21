namespace GMS.Platform.Interfaces;

/// <summary>Resolves and persists TOTP secrets via Key Vault reference or local encrypted store.</summary>
public interface IPlatformMfaSecretStore
{
    /// <summary>Returns a storage reference string to persist on PlatformAdminUser.MfaSecret.</summary>
    string StoreSecret(Guid userId, string rawBase32Secret);

    /// <summary>Resolves the raw Base32 TOTP secret from a stored reference.</summary>
    string? ResolveSecret(string? storedReference);
}
