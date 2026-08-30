namespace GMS.Tests;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Api.Authorization;
using GMS.Application.DTOs.Admin;
using GMS.Application.DTOs.Audit;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

/// <summary>
/// Every Identity job × every permission in <see cref="Permissions.All"/>.
/// Unticking a task on Roles must drop it from Resolve (login/refresh JWT).
/// Owner and Member stay locked. Sibling jobs are unchanged.
/// </summary>
public class RolePermissionMatrixTests
{
    private static readonly string[] AllRoles = { "Owner", "Manager", "Receptionist", "Trainer", "Member" };
    private static readonly string[] EditableRoles = { "Manager", "Receptionist", "Trainer" };

    public static IEnumerable<object[]> EveryRoleAndPermission()
    {
        foreach (var role in AllRoles)
            foreach (var permission in Permissions.All)
                yield return new object[] { role, permission };
    }

    public static IEnumerable<object[]> EveryEditableRoleAndPermission()
    {
        foreach (var role in EditableRoles)
            foreach (var permission in Permissions.All)
                yield return new object[] { role, permission };
    }

    public static IEnumerable<object[]> EveryPermission()
    {
        foreach (var permission in Permissions.All)
            yield return new object[] { permission };
    }

    [Fact]
    public void Matrix_CoversEveryJobAndEveryTask()
    {
        Assert.Equal(41, Permissions.All.Count);
        Assert.Equal(5 * 41, EveryRoleAndPermission().Count());
        Assert.Equal(3 * 41, EveryEditableRoleAndPermission().Count());
    }

    [Theory]
    [MemberData(nameof(EveryRoleAndPermission))]
    public void LoginResolve_Prevent_DropsTask_ExceptOwnerLock(string role, string permission)
    {
        var provider = new DefaultPermissionProvider();
        var defaults = provider.GetPermissions(new[] { role });
        var remaining = defaults.Where(p => p != permission).ToList();
        var overlay = OverlayFor(role, remaining);
        var effective = RolePermissionResolver.Resolve(new[] { role }, provider, overlay);

        if (role == "Owner")
        {
            Assert.Contains(permission, effective);
            Assert.Equal(Permissions.All.Count, effective.Count);
            return;
        }

        Assert.DoesNotContain(permission, effective);
        foreach (var kept in remaining)
            Assert.Contains(kept, effective);
    }

    [Theory]
    [MemberData(nameof(EveryEditableRoleAndPermission))]
    public async Task Save_Prevent_IsAppliedToThatJob_Only(string role, string permission)
    {
        var provider = new DefaultPermissionProvider();
        var (ctx, svc, _, _, tenantId) = CreateSut();
        var remaining = provider.GetPermissions(new[] { role }).Where(p => p != permission).ToList();

        var saved = await svc.UpdateRoleAsync(tenantId, role, new UpdateRolePermissionsRequest
        {
            Permissions = remaining
        });
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.DoesNotContain(permission, saved.Data!.Permissions);
        foreach (var kept in remaining)
            Assert.Contains(kept, saved.Data.Permissions);

        var tenant = await ctx.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId);
        var overlay = RolePermissionResolver.ParseOverlay(tenant.Settings);

        Assert.DoesNotContain(
            permission,
            RolePermissionResolver.Resolve(new[] { role }, provider, overlay));

        foreach (var other in EditableRoles.Where(r => r != role))
        {
            var expected = provider.GetPermissions(new[] { other });
            var actual = RolePermissionResolver.Resolve(new[] { other }, provider, overlay);
            Assert.True(expected.SetEquals(actual), $"{other} must keep defaults when {role} is edited");
        }

        var owner = RolePermissionResolver.Resolve(new[] { "Owner" }, provider, overlay);
        Assert.Equal(Permissions.All.Count, owner.Count);
        var member = RolePermissionResolver.Resolve(new[] { "Member" }, provider, overlay);
        Assert.Empty(member);
    }

    [Theory]
    [MemberData(nameof(EveryEditableRoleAndPermission))]
    public async Task Save_Grant_AddsTaskToThatJob_Only(string role, string permission)
    {
        var provider = new DefaultPermissionProvider();
        var (ctx, svc, _, _, tenantId) = CreateSut();
        var granted = provider.GetPermissions(new[] { role }).Append(permission).Distinct().ToList();

        var saved = await svc.UpdateRoleAsync(tenantId, role, new UpdateRolePermissionsRequest
        {
            Permissions = granted
        });
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Contains(permission, saved.Data!.Permissions);

        var tenant = await ctx.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId);
        var overlay = RolePermissionResolver.ParseOverlay(tenant.Settings);
        Assert.Contains(
            permission,
            RolePermissionResolver.Resolve(new[] { role }, provider, overlay));

        foreach (var other in EditableRoles.Where(r => r != role))
        {
            var expected = provider.GetPermissions(new[] { other });
            var actual = RolePermissionResolver.Resolve(new[] { other }, provider, overlay);
            Assert.True(expected.SetEquals(actual), $"{other} must keep defaults when {role} is edited");
        }
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Receptionist")]
    [InlineData("Trainer")]
    public async Task Save_PreventAllTasks_LeavesZeroPermissions(string role)
    {
        var provider = new DefaultPermissionProvider();
        var (ctx, svc, _, _, tenantId) = CreateSut();
        var saved = await svc.UpdateRoleAsync(tenantId, role, new UpdateRolePermissionsRequest
        {
            Permissions = new List<string>()
        });
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Empty(saved.Data!.Permissions);
        Assert.True(saved.Data.IsCustomized);

        var catalog = await svc.GetCatalogAsync(tenantId);
        Assert.Empty(catalog.Data!.Roles.Single(r => r.Id == role).Permissions);

        var tenant = await ctx.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId);
        var overlay = RolePermissionResolver.ParseOverlay(tenant.Settings);
        Assert.Empty(RolePermissionResolver.Resolve(new[] { role }, provider, overlay));
        Assert.Equal(
            Permissions.All.Count,
            RolePermissionResolver.Resolve(new[] { "Owner" }, provider, overlay).Count);
    }

    [Theory]
    [MemberData(nameof(EveryPermission))]
    public async Task JwtGate_MissingClaim_Denies(string permission)
    {
        var allowed = await AuthorizeAsync(Array.Empty<string>(), permission);
        Assert.False(allowed);
    }

    [Theory]
    [MemberData(nameof(EveryPermission))]
    public async Task JwtGate_PresentClaim_Allows(string permission)
    {
        var allowed = await AuthorizeAsync(new[] { permission }, permission);
        Assert.True(allowed);
    }

    [Fact]
    public async Task AnyPermissionGate_AllowsTrainerClassPermissionWithoutMembersModule()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(Permissions.ClaimType, Permissions.ClassesView)
        }, "Test"));
        var requirement = new AnyPermissionRequirement(new[]
        {
            Permissions.MembersView, Permissions.ClassesView
        });
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

        await new AnyPermissionAuthorizationHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Theory]
    [MemberData(nameof(EveryEditableRoleAndPermission))]
    public async Task PreventThenJwt_DeniesThatTask(string role, string permission)
    {
        var provider = new DefaultPermissionProvider();
        var remaining = provider.GetPermissions(new[] { role }).Where(p => p != permission).ToList();
        var overlay = OverlayFor(role, remaining);
        var jwtPerms = RolePermissionResolver.Resolve(new[] { role }, provider, overlay);

        var allowed = await AuthorizeAsync(jwtPerms, permission);
        Assert.False(allowed);

        foreach (var kept in remaining)
            Assert.True(await AuthorizeAsync(jwtPerms, kept), $"{role} must still pass {kept}");
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> OverlayFor(
        string role, IReadOnlyList<string> keys)
    {
        var overlay = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (RolePermissionResolver.IsEditable(role))
            overlay[role] = RolePermissionResolver.NormalizeKeys(keys);
        var json = RolePermissionResolver.WriteOverlay(null, overlay);
        return RolePermissionResolver.ParseOverlay(json);
    }

    private static async Task<bool> AuthorizeAsync(IEnumerable<string> jwtPerms, string required)
    {
        var claims = jwtPerms.Select(p => new Claim(Permissions.ClaimType, p)).ToList();
        claims.Add(new Claim(ClaimTypes.Role, "Manager"));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        var handler = new PermissionAuthorizationHandler();
        var context = new AuthorizationHandlerContext(
            new[] { new PermissionRequirement(required) },
            user,
            resource: null);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    private sealed class RecordingAudit : IAuditService
    {
        public Task LogAsync(
            string action,
            string? entityType = null,
            Guid? entityId = null,
            object? before = null,
            object? after = null,
            Guid? tenantIdOverride = null) => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<AuditEventDto>>.Failure("n/a"));
    }

    private sealed class RecordingCache : IPermissionCacheService
    {
        public Task<IReadOnlySet<string>?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>?>(null);

        public Task SetAsync(Guid tenantId, Guid userId, IReadOnlySet<string> permissions, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task InvalidateAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static Tenant NewTenant(Guid id) => new()
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
        CreatedAtUtc = DateTime.UtcNow
    };

    private static (GymFlowProDbContext ctx, RolePermissionService svc, RecordingCache cache, RecordingAudit audit, Guid tenantId)
        CreateSut()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Gym", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);
        ctx.Tenants.Add(NewTenant(tenantId));
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
}
