namespace GMS.Tests;

using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using GMS.Api.Authorization;
using GMS.Core.Constants;
using GMS.Infrastructure.Services;

/// <summary>
/// Exercises the real dynamic policy provider + authorization handler that controllers rely on
/// via [HasPermission], without spinning up a full HTTP host. AuthorizeAsync's Succeeded/Failed
/// result is exactly what turns into a 200 vs 403 in ASP.NET Core's authorization middleware.
/// </summary>
public class PermissionAuthorizationTests
{
    private readonly IAuthorizationService _authorizationService;
    private readonly DefaultPermissionProvider _permissionProvider = new();

    public PermissionAuthorizationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddAuthorization();

        _authorizationService = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private ClaimsPrincipal BuildPrincipal(params string[] roles)
    {
        var permissions = _permissionProvider.GetPermissions(roles);
        var claims = permissions.Select(p => new Claim(Permissions.ClaimType, p)).ToList();
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    private Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal principal, string permission) =>
        _authorizationService.AuthorizeAsync(principal, PermissionPolicyProvider.PolicyPrefix + permission);

    [Fact]
    public async Task Receptionist_CanCallManualCheckin()
    {
        var receptionist = BuildPrincipal("Receptionist");

        var result = await AuthorizeAsync(receptionist, Permissions.CheckinManual);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Receptionist_Gets403OnFreeze()
    {
        var receptionist = BuildPrincipal("Receptionist");

        var result = await AuthorizeAsync(receptionist, Permissions.MembershipsFreeze);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Receptionist_Gets403OnPlanEdit()
    {
        var receptionist = BuildPrincipal("Receptionist");

        var result = await AuthorizeAsync(receptionist, Permissions.PlansManage);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Receptionist_Gets403OnFinancialAnalytics()
    {
        var receptionist = BuildPrincipal("Receptionist");

        var result = await AuthorizeAsync(receptionist, Permissions.ReportsFinancialView);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Manager_PassesFreeze()
    {
        var manager = BuildPrincipal("Manager");

        var result = await AuthorizeAsync(manager, Permissions.MembershipsFreeze);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Manager_FailsPlansManage()
    {
        var manager = BuildPrincipal("Manager");

        var result = await AuthorizeAsync(manager, Permissions.PlansManage);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Owner_PassesEveryPermission()
    {
        var owner = BuildPrincipal("Owner");

        foreach (var permission in Permissions.All)
        {
            var result = await AuthorizeAsync(owner, permission);
            Assert.True(result.Succeeded, $"Owner should pass {permission}");
        }
    }

    [Fact]
    public async Task Trainer_FailsCheckinManual_UnlessCheckinPermissionPresent()
    {
        // Trainer's only new grant per spec is checkin.manual — other permissions must fail.
        var trainer = BuildPrincipal("Trainer");

        Assert.True((await AuthorizeAsync(trainer, Permissions.CheckinManual)).Succeeded);
        Assert.False((await AuthorizeAsync(trainer, Permissions.MembershipsFreeze)).Succeeded);
        Assert.False((await AuthorizeAsync(trainer, Permissions.PlansManage)).Succeeded);
    }
}
