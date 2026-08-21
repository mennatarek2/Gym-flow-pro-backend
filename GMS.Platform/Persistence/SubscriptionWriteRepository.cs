namespace GMS.Platform.Persistence;

using Microsoft.EntityFrameworkCore;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;

public class SubscriptionWriteRepository : ISubscriptionWriteRepository
{
    private readonly PlatformDbContext _db;

    public SubscriptionWriteRepository(PlatformDbContext db)
    {
        _db = db;
    }

    public async Task SaveWithChangeAsync(
        PlatformSubscription subscription,
        SubscriptionChange change,
        CancellationToken cancellationToken = default)
    {
        if (change.SubscriptionId == Guid.Empty)
            change.SubscriptionId = subscription.Id;
        if (change.TenantId == Guid.Empty)
            change.TenantId = subscription.TenantId;

        subscription.UpdatedAtUtc = DateTime.UtcNow;
        change.CreatedAtUtc = DateTime.UtcNow;

        async Task PersistAsync()
        {
            var entry = _db.Entry(subscription);
            if (entry.State == EntityState.Detached)
            {
                var exists = await _db.Subscriptions.AnyAsync(s => s.Id == subscription.Id, cancellationToken);
                if (exists)
                    _db.Subscriptions.Update(subscription);
                else
                    _db.Subscriptions.Add(subscription);
            }

            _db.SubscriptionChanges.Add(change);
            await _db.SaveChangesAsync(cancellationToken);
        }

        // InMemory provider cannot open transactions — skip for tests.
        if (!_db.Database.IsRelational())
        {
            await PersistAsync();
            return;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await PersistAsync();
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task<PlatformSubscription?> GetLiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return _db.Subscriptions
            .FirstOrDefaultAsync(
                s => s.TenantId == tenantId &&
                     (s.Status == SubscriptionStatuses.Trialing ||
                      s.Status == SubscriptionStatuses.Active ||
                      s.Status == SubscriptionStatuses.PastDue),
                cancellationToken);
    }

    public Task<PlatformSubscription?> GetByIdAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        return _db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken);
    }

    public async Task<string?> GetPendingDowngradeTierAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _db.SubscriptionChanges
            .Where(c =>
                c.SubscriptionId == subscriptionId &&
                c.ChangeType == SubscriptionChangeTypes.Downgrade &&
                c.EffectiveAtUtc > now)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => c.ToTier)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
