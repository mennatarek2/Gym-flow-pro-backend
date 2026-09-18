namespace GMS.Platform.Persistence;

using Microsoft.EntityFrameworkCore;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;

/// <summary>Mirrors SubscriptionWriteRepository's transactional shape - see that class for why.</summary>
public class LocalLicenseWriteRepository : ILocalLicenseWriteRepository
{
    private readonly PlatformDbContext _db;

    public LocalLicenseWriteRepository(PlatformDbContext db)
    {
        _db = db;
    }

    public async Task SaveAsync(
        LocalLicense license,
        LocalLicenseChange change,
        LocalInstallation? installation = null,
        LocalActivationAttempt? attempt = null,
        CancellationToken cancellationToken = default)
    {
        if (change.LicenseId == Guid.Empty)
            change.LicenseId = license.Id;
        if (attempt != null && attempt.LicenseId == null)
            attempt.LicenseId = license.Id;

        license.UpdatedAtUtc = DateTime.UtcNow;
        change.CreatedAtUtc = DateTime.UtcNow;
        if (attempt != null)
            attempt.CreatedAtUtc = DateTime.UtcNow;

        async Task PersistAsync()
        {
            var entry = _db.Entry(license);
            if (entry.State == EntityState.Detached)
            {
                var exists = await _db.LocalLicenses.AnyAsync(l => l.Id == license.Id, cancellationToken);
                if (exists) _db.LocalLicenses.Update(license);
                else _db.LocalLicenses.Add(license);
            }

            _db.LocalLicenseChanges.Add(change);

            if (installation != null)
            {
                var instEntry = _db.Entry(installation);
                if (instEntry.State == EntityState.Detached)
                {
                    var exists = await _db.LocalInstallations.AnyAsync(i => i.Id == installation.Id, cancellationToken);
                    if (exists) _db.LocalInstallations.Update(installation);
                    else _db.LocalInstallations.Add(installation);
                }
            }

            if (attempt != null)
                _db.LocalActivationAttempts.Add(attempt);

            await _db.SaveChangesAsync(cancellationToken);
        }

        if (!_db.Database.IsRelational() || _db.Database.CurrentTransaction != null)
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

    public Task<LocalLicense?> GetByKeyAsync(string licenseKey, CancellationToken cancellationToken = default) =>
        _db.LocalLicenses.FirstOrDefaultAsync(l => l.LicenseKey == licenseKey, cancellationToken);

    public Task<LocalLicense?> GetByIdAsync(Guid id, bool includeInstallations = false, CancellationToken cancellationToken = default)
    {
        var query = _db.LocalLicenses.AsQueryable();
        if (includeInstallations)
            query = query.Include(l => l.Installations).Include(l => l.Changes);
        return query.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
    }

    public Task<List<LocalLicense>> ListAsync(CancellationToken cancellationToken = default) =>
        _db.LocalLicenses.OrderByDescending(l => l.CreatedAtUtc).ToListAsync(cancellationToken);

    public Task<List<LocalInstallation>> ListActiveInstallationsAsync(CancellationToken cancellationToken = default) =>
        _db.LocalInstallations.AsNoTracking()
            .Where(i => i.Status == Constants.LocalInstallationStatuses.Active)
            .ToListAsync(cancellationToken);

    public Task<List<LocalInstallation>> GetActiveInstallationsAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        _db.LocalInstallations
            .Where(i => i.LicenseId == licenseId && i.Status == Constants.LocalInstallationStatuses.Active)
            .ToListAsync(cancellationToken);

    public Task<LocalInstallation?> GetInstallationAsync(Guid licenseId, string installationId, CancellationToken cancellationToken = default) =>
        _db.LocalInstallations.FirstOrDefaultAsync(
            i => i.LicenseId == licenseId && i.InstallationId == installationId, cancellationToken);

    public async Task LockLicenseRowAsync(Guid licenseId, CancellationToken cancellationToken = default)
    {
        await _db.Database
            .SqlQuery<Guid>($"SELECT [Id] AS [Value] FROM [platform].[local_licenses] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {licenseId}")
            .ToListAsync(cancellationToken);
    }

    public async Task LogAttemptAsync(LocalActivationAttempt attempt, CancellationToken cancellationToken = default)
    {
        attempt.CreatedAtUtc = DateTime.UtcNow;
        _db.LocalActivationAttempts.Add(attempt);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
