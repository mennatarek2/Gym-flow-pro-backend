namespace GMS.Platform.Interfaces;

public interface IRollUpTenantUsageJob
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
