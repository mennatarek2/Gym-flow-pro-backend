namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Provisioning;

/// <summary>
/// Creates Tenant + Owner + defaults. Cloud provision starts a platform trial;
/// Local Lifetime Setup sets StartTrial = false.
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
