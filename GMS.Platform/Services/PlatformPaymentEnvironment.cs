namespace GMS.Platform.Services;

using Microsoft.Extensions.Hosting;

/// <summary>
/// Environment-aware payment safety: Production/Staging fail closed; Development may use explicit mocks.
/// </summary>
public static class PlatformPaymentEnvironment
{
    public static bool AllowMockPayments(IHostEnvironment environment) =>
        environment.IsDevelopment();

    public static bool RequireConfiguredCredentials(IHostEnvironment environment) =>
        !environment.IsDevelopment();
}
