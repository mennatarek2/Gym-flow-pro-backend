namespace GMS.Platform.Interfaces;

using GMS.Platform.DTOs;

public interface IPlatformAuthService
{
    Task<PlatformLoginResult> LoginAsync(PlatformLoginRequest request, string? ipAddress);
    Task<PlatformLoginResult> CompleteMfaSetupAsync(PlatformMfaSetupRequest request, string? ipAddress);
}
