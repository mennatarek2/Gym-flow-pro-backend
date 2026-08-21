namespace GMS.Infrastructure.Services;

using GMS.Core.Interfaces;

/// <summary>
/// Implementation of tenant context for multi-tenant support.
/// </summary>
public class TenantContext : ITenantContext
{
    public Guid TenantId { get; private set; }
    public string? TenantName { get; private set; }
    public string? TimeZone { get; private set; }
    public bool IsInitialized { get; private set; }

    public void SetTenant(Guid tenantId, string tenantName, string timeZone)
    {
        TenantId = tenantId;
        TenantName = tenantName;
        TimeZone = timeZone;
        IsInitialized = true;
    }

    public void Clear()
    {
        TenantId = Guid.Empty;
        TenantName = null;
        TimeZone = null;
        IsInitialized = false;
    }
}
