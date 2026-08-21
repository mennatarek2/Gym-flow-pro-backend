namespace GMS.Core.Constants;

/// <summary>Tenant JWT claim set when a platform operator is impersonating support access.</summary>
public static class ImpersonationClaims
{
    public const string ImpersonatedByPlatformUserId = "impersonated_by_platform_user_id";
    public const string TokenUse = "token_use";
    public const string TokenUseImpersonation = "tenant_impersonation";
    public const int LifetimeMinutes = 30;
}
