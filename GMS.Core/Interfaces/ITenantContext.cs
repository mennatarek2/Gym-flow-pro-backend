namespace GMS.Core.Interfaces;

/// <summary>
/// Provides tenant context information for multi-tenant operations.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    string? TenantName { get; }
    string? TimeZone { get; }
    bool IsInitialized { get; }

    void SetTenant(Guid tenantId, string tenantName, string timeZone);
    void Clear();
}
