namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Admin;
using GMS.Application.DTOs.Audit;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class RolePermissionOverlayTests
{
    private sealed class RecordingAudit : IAuditService
    {
        public string? LastAction { get; private set; }

        public Task LogAsync(
            string action,
            string? entityType = null,
            Guid? entityId = null,
            object? before = null,
            object? after = null,
            Guid? tenantIdOverride = null)
        {
            LastAction = action;
            return Task.CompletedTask;
        }

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<AuditEventDto>>.Failure("n/a"));
    }

    private sealed class RecordingCache : IPermissionCacheService
    {
        public List<Guid> Invalidated { get; } = new();

        public Task<IReadOnlySet<string>?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>?>(null);

        public Task SetAsync(Guid tenantId, Guid userId, IReadOnlySet<string> permissions, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InvalidateAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
        {
            Invalidated.Add(userId);
            return Task.CompletedTask;
        }
    }

    private static Tenant NewTenant(Guid id, string? settings = null) => new()
    {
        Id = id,
        Name = "Gym",
        NameAr = "صالة",
        GymCode = "GYM-" + id.ToString("N")[..6],
        City = "Cairo",
        Address = "x",
        PhoneNumber = "01000000000",
        Email = id.ToString("N")[..8] + "@test.local",
        SubscriptionStartDate = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow,
        Settings = settings
    };

    private static (GymFlowProDbContext ctx, RolePermissionService svc, RecordingCache cache, RecordingAudit audit, Guid tenantId)
        CreateSut(string? settingsJson = null, Guid? receptionistUserId = null)
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Gym", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);
        ctx.Tenants.Add(NewTenant(tenantId, settingsJson));
        if (receptionistUserId is Guid uid)
        {
            ctx.AppUsers.Add(new AppUser
            {
                TenantId = tenantId,
                UserId = uid.ToString(),
                FirstName = "R",
                LastName = "Desk",
                Email = "r@test.local",
                Role = "Receptionist",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        ctx.SaveChanges();

        var audit = new RecordingAudit();
        var cache = new RecordingCache();
        var svc = new RolePermissionService(
            ctx,
            new DefaultPermissionProvider(),
            cache,
            audit,
            NullLogger<RolePermissionService>.Instance);
        return (ctx, svc, cache, audit, tenantId);
    }

    [Fact]
    public void Resolver_NoOverlay_UsesProviderDefaults()
    {
        var provider = new DefaultPermissionProvider();
        var overlay = RolePermissionResolver.ParseOverlay(null);
        var rec = RolePermissionResolver.Resolve(new[] { "Receptionist" }, provider, overlay);
        Assert.Equal(13, rec.Count);
        Assert.Contains(Permissions.SalesSell, rec);
        Assert.DoesNotContain(Permissions.PaymentsRefundApprove, rec);
    }

    [Fact]
    public void Resolver_OverlayReplacesEditableRole()
    {
        var provider = new DefaultPermissionProvider();
        var json = """{"role_permissions":{"Receptionist":["members.view","sales.sell","not.a.key"]}}""";
        var overlay = RolePermissionResolver.ParseOverlay(json);
        var rec = RolePermissionResolver.Resolve(new[] { "Receptionist" }, provider, overlay);
        Assert.Equal(2, rec.Count);
        Assert.Contains(Permissions.MembersView, rec);
        Assert.Contains(Permissions.SalesSell, rec);
        Assert.DoesNotContain("not.a.key", rec);
    }

    [Fact]
    public void Resolver_IgnoresOwnerAndMemberOverlay()
    {
        var provider = new DefaultPermissionProvider();
        var json = """{"role_permissions":{"Owner":[],"Member":["members.view"],"Trainer":["checkin.manual","members.view"]}}""";
        var overlay = RolePermissionResolver.ParseOverlay(json);
        Assert.False(overlay.ContainsKey("Owner"));
        Assert.False(overlay.ContainsKey("Member"));
        var owner = RolePermissionResolver.Resolve(new[] { "Owner" }, provider, overlay);
        Assert.Equal(Permissions.All.Count, owner.Count);
        var trainer = RolePermissionResolver.Resolve(new[] { "Trainer" }, provider, overlay);
        Assert.Contains(Permissions.MembersView, trainer);
        Assert.Contains(Permissions.CheckinManual, trainer);
    }

    [Fact]
    public void Resolver_EmptyArrayMeansZeroPerms()
    {
        var provider = new DefaultPermissionProvider();
        var json = """{"role_permissions":{"Trainer":[]}}""";
        var overlay = RolePermissionResolver.ParseOverlay(json);
        var trainer = RolePermissionResolver.Resolve(new[] { "Trainer" }, provider, overlay);
        Assert.Empty(trainer);
    }

    [Fact]
    public async Task Catalog_WithoutOverlay_MatchesDefaults()
    {
        var (_, svc, _, _, tenantId) = CreateSut();
        var result = await svc.GetCatalogAsync(tenantId);
        Assert.True(result.IsSuccess);
        var rec = result.Data!.Roles.Single(r => r.Id == "Receptionist");
        Assert.True(rec.Editable);
        Assert.False(rec.IsCustomized);
        Assert.Equal(13, rec.Permissions.Count);
        var owner = result.Data.Roles.Single(r => r.Id == "Owner");
        Assert.False(owner.Editable);
        Assert.Equal(Permissions.All.Count, owner.Permissions.Count);
        Assert.DoesNotContain(result.Data.Roles, r => r.Id == "Member");
    }

    [Fact]
    public async Task Update_Receptionist_PersistsAndInvalidates()
    {
        var recUser = Guid.NewGuid();
        var (ctx, svc, cache, audit, tenantId) = CreateSut(receptionistUserId: recUser);
        var result = await svc.UpdateRoleAsync(tenantId, "receptionist", new UpdateRolePermissionsRequest
        {
            Permissions = new List<string> { Permissions.MembersView, Permissions.SalesSell, "bogus" }
        });
        Assert.True(result.IsSuccess);
        Assert.True(result.Data!.IsCustomized);
        Assert.Equal(2, result.Data.Permissions.Count);
        Assert.Equal("roles.update", audit.LastAction);
        Assert.Contains(recUser, cache.Invalidated);

        var tenant = await ctx.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId);
        Assert.Contains("members.view", tenant.Settings);
        Assert.DoesNotContain("bogus", tenant.Settings);
    }

    [Fact]
    public async Task Update_Owner_IsLocked()
    {
        var (_, svc, _, _, tenantId) = CreateSut();
        var result = await svc.UpdateRoleAsync(tenantId, "Owner", new UpdateRolePermissionsRequest
        {
            Permissions = new List<string>()
        });
        Assert.False(result.IsSuccess);
        Assert.StartsWith("ROLE_LOCKED|", result.Error);
    }

    [Fact]
    public async Task Update_SameAsDefault_ClearsOverlay()
    {
        var json = """{"role_permissions":{"Trainer":["members.view"]}}""";
        var (_, svc, _, _, tenantId) = CreateSut(json);
        var result = await svc.UpdateRoleAsync(tenantId, "Trainer", new UpdateRolePermissionsRequest
        {
            Permissions = new List<string> { Permissions.CheckinManual }
        });
        Assert.True(result.IsSuccess);
        Assert.False(result.Data!.IsCustomized);
        Assert.Equal(new[] { Permissions.CheckinManual }, result.Data.Permissions);
    }

    [Fact]
    public async Task Reset_RestoresDefault()
    {
        var json = """{"role_permissions":{"Trainer":["members.view"]}}""";
        var (_, svc, _, audit, tenantId) = CreateSut(json);
        var result = await svc.ResetRoleAsync(tenantId, "Trainer");
        Assert.True(result.IsSuccess);
        Assert.False(result.Data!.IsCustomized);
        Assert.Equal(new[] { Permissions.CheckinManual }, result.Data.Permissions);
        Assert.Equal("roles.reset", audit.LastAction);
    }
}
