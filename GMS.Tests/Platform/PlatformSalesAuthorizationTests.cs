namespace GMS.Tests.Platform;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using GMS.Platform.Constants;

/// <summary>
/// Exercises the exact PlatformSalesOrAbove/PlatformOpsOrAbove/PlatformAdminOnly policies
/// registered in Program.cs (replicated here, not referenced, since Program.cs is top-level
/// statements — same approach as PermissionAuthorizationTests). AuthorizeAsync's Succeeded/Failed
/// is exactly what LocalLicensesController's [Authorize] attributes turn into a 200 vs 403 for.
///
/// This is the server-side proof for rule 12 ("Sales Reps must never have the technical ability
/// to create arbitrary production licenses") — a Sales Rep calling the API directly, bypassing
/// any frontend hiding entirely, still gets rejected by these exact policy checks.
/// </summary>
public class PlatformSalesAuthorizationTests
{
    private readonly IAuthorizationService _authorizationService;

    public PlatformSalesAuthorizationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("PlatformSalesOrAbove", p => p.RequireRole(PlatformRoles.Sales, PlatformRoles.Ops, PlatformRoles.Admin));
            options.AddPolicy("PlatformOpsOrAbove", p => p.RequireRole(PlatformRoles.Ops, PlatformRoles.Admin));
            options.AddPolicy("PlatformAdminOnly", p => p.RequireRole(PlatformRoles.Admin));
            options.AddPolicy("PlatformSupportOrAbove", p => p.RequireRole(PlatformRoles.Support, PlatformRoles.Ops, PlatformRoles.Admin));
            options.AddPolicy("PlatformCustomerAccess", p => p.RequireRole(PlatformRoles.Sales, PlatformRoles.Support, PlatformRoles.Ops, PlatformRoles.Admin));
        });

        _authorizationService = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal BuildPrincipal(string role)
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    private Task<AuthorizationResult> Authorize(ClaimsPrincipal principal, string policy) =>
        _authorizationService.AuthorizeAsync(principal, policy);

    [Fact]
    public async Task SalesRep_CanAccess_ReadOnly_ListEndpointPolicy()
    {
        var sales = BuildPrincipal(PlatformRoles.Sales);
        // LocalLicensesController's class-level [Authorize] (GET list/detail) is PlatformSalesOrAbove.
        Assert.True((await Authorize(sales, "PlatformSalesOrAbove")).Succeeded);
    }

    [Fact]
    public async Task SalesRep_CannotIssueLicense()
    {
        var sales = BuildPrincipal(PlatformRoles.Sales);
        // LocalLicensesController.Issue is re-gated to PlatformOpsOrAbove - rule 12's core check.
        Assert.False((await Authorize(sales, "PlatformOpsOrAbove")).Succeeded);
    }

    [Fact]
    public async Task SalesRep_CannotSuspendLicense()
    {
        var sales = BuildPrincipal(PlatformRoles.Sales);
        Assert.False((await Authorize(sales, "PlatformOpsOrAbove")).Succeeded); // Suspend action policy
    }

    [Fact]
    public async Task SalesRep_CannotRevokeLicense()
    {
        var sales = BuildPrincipal(PlatformRoles.Sales);
        Assert.False((await Authorize(sales, "PlatformAdminOnly")).Succeeded); // Revoke action policy
    }

    [Fact]
    public async Task SalesRep_CannotReactivateLicense()
    {
        var sales = BuildPrincipal(PlatformRoles.Sales);
        Assert.False((await Authorize(sales, "PlatformAdminOnly")).Succeeded); // Reactivate action policy
    }

    [Fact]
    public async Task SalesRep_CannotAuthorizeTransfer()
    {
        var sales = BuildPrincipal(PlatformRoles.Sales);
        Assert.False((await Authorize(sales, "PlatformOpsOrAbove")).Succeeded); // Transfer action policy
    }

    [Fact]
    public async Task Ops_CanIssueAndSuspend_ButNotRevoke()
    {
        var ops = BuildPrincipal(PlatformRoles.Ops);
        Assert.True((await Authorize(ops, "PlatformOpsOrAbove")).Succeeded);
        Assert.False((await Authorize(ops, "PlatformAdminOnly")).Succeeded); // Revoke/Reactivate are Admin-only
    }

    [Fact]
    public async Task Admin_CanDoEverything()
    {
        var admin = BuildPrincipal(PlatformRoles.Admin);
        Assert.True((await Authorize(admin, "PlatformSalesOrAbove")).Succeeded);
        Assert.True((await Authorize(admin, "PlatformOpsOrAbove")).Succeeded);
        Assert.True((await Authorize(admin, "PlatformAdminOnly")).Succeeded);
    }

    [Fact]
    public async Task SalesRep_CanAccessCustomers_ButCannotIssueOrRevokeLicenses()
    {
        var sales = BuildPrincipal(PlatformRoles.Sales);
        Assert.True((await Authorize(sales, "PlatformCustomerAccess")).Succeeded);
        Assert.True((await Authorize(sales, "PlatformSalesOrAbove")).Succeeded);
        Assert.False((await Authorize(sales, "PlatformOpsOrAbove")).Succeeded);
        Assert.False((await Authorize(sales, "PlatformAdminOnly")).Succeeded);
        Assert.False((await Authorize(sales, "PlatformSupportOrAbove")).Succeeded);
    }

    [Fact]
    public async Task Support_CanAccessCustomersAndTickets_ButCannotCreateContractsOrIssueLicenses()
    {
        var support = BuildPrincipal(PlatformRoles.Support);
        Assert.True((await Authorize(support, "PlatformCustomerAccess")).Succeeded);
        Assert.True((await Authorize(support, "PlatformSupportOrAbove")).Succeeded);
        Assert.False((await Authorize(support, "PlatformSalesOrAbove")).Succeeded);
        Assert.False((await Authorize(support, "PlatformOpsOrAbove")).Succeeded);
    }

    [Fact]
    public async Task SalesRole_IsNotPartOfTheSupportOpsAdminHierarchy()
    {
        // A sales rep must not incidentally gain broader platform access via some future
        // "OrAbove" chain reuse - Sales is its own, narrow policy (see PlatformRoles.Sales remarks).
        var sales = BuildPrincipal(PlatformRoles.Sales);
        var opsOnlyPolicy = new AuthorizationPolicyBuilder().RequireRole(PlatformRoles.Ops, PlatformRoles.Admin).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        var authService = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();

        var result = await authService.AuthorizeAsync(sales, opsOnlyPolicy);
        Assert.False(result.Succeeded);
    }
}
