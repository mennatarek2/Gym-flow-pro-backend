namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Admin;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Entities.Identity;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

/// <summary>
/// Confirms staff-by-id operations cannot read/mutate AspNetUsers belonging to another tenant.
/// </summary>
public class AdminServiceTenantIsolationTests
{
    private sealed class NoopPermissionCache : IPermissionCacheService
    {
        public Task<IReadOnlySet<string>?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>?>(null);

        public Task SetAsync(Guid tenantId, Guid userId, IReadOnlySet<string> permissions, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InvalidateAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class AllowAllTiers : ITierEnforcementService
    {
        public Task<CapCheckResult> CheckCapAsync(Guid tenantId, string metric, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapCheckResult { Allowed = true, Metric = metric });
    }

    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private sealed class NoopFiles : IFileStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string folder) =>
            Task.FromResult($"/uploads/{folder}/{fileName}");
        public Task DeleteAsync(string fileUrl) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string fileUrl) => Task.FromResult(false);
    }

    [Fact]
    public async Task GetUpdateDeleteReset_ForeignTenantStaff_ReturnsNotFound()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantA, "A", "Africa/Cairo");
        await using var db = new GymFlowProDbContext(options, tenantContext);

        var foreign = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "foreign@test.local",
            Email = "foreign@test.local",
            NormalizedEmail = "FOREIGN@TEST.LOCAL",
            NormalizedUserName = "FOREIGN@TEST.LOCAL",
            TenantId = tenantB,
            FirstName = "Other",
            LastName = "Gym",
            EmailConfirmed = true,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(foreign);
        await db.SaveChangesAsync();

        // UserManager unused when Id+TenantId miss — match Cp4 staff cap pattern.
        var sut = new AdminService(
            db,
            userManager: null!,
            NullLogger<AdminService>.Instance,
            new NoopPermissionCache(),
            new AllowAllTiers(),
            new NoOpAudit(),
            new NoopFiles());

        Assert.False((await sut.GetStaffUserByIdAsync(tenantA, foreign.Id)).IsSuccess);
        Assert.False((await sut.UpdateStaffUserAsync(tenantA, foreign.Id, new UpdateStaffRequest
        {
            FullName = "Hacked",
            Role = "manager",
            IsActive = true
        })).IsSuccess);
        Assert.False((await sut.DeleteStaffUserAsync(tenantA, foreign.Id)).IsSuccess);
        Assert.False((await sut.ResetStaffPasswordAsync(tenantA, foreign.Id, "NewPass123!")).IsSuccess);
        Assert.False((await sut.GetStaffActivityAsync(tenantA, foreign.Id)).IsSuccess);
        await using var photo = new MemoryStream(new byte[] { 1 });
        Assert.False((await sut.SetStaffPhotoAsync(tenantA, foreign.Id, photo, "x.jpg", "image/jpeg")).IsSuccess);

        var still = await db.Users.SingleAsync(u => u.Id == foreign.Id);
        Assert.Equal(tenantB, still.TenantId);
        Assert.Equal("Other", still.FirstName);
        Assert.True(still.IsActive);
    }
}
