namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Provisioning;

/// <summary>
/// Production gym onboarding — creates Tenant + Owner + defaults + platform trial.
/// Not the Development <c>DataSeeder</c>.
/// </summary>
public interface ITenantProvisioningService
{
    Task<Result<ProvisionTenantResponse>> ProvisionAsync(
        ProvisionTenantRequest request,
        Guid actorPlatformUserId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);
}
