namespace GMS.Core.Interfaces;

/// <summary>Real-time push for member-store order lifecycle (desk dashboards).</summary>
public interface IMemberOrderNotifier
{
    Task NotifyCreatedAsync(Guid tenantId, Guid orderId, string orderNumber, Guid memberId, string memberName, CancellationToken ct = default);

    Task NotifyStatusChangedAsync(
        Guid tenantId,
        Guid orderId,
        string orderNumber,
        string status,
        Guid memberId,
        CancellationToken ct = default);
}
