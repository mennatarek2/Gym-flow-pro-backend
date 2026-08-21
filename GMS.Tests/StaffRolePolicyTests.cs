namespace GMS.Tests;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Backend AnyStaff is Owner|Manager|Trainer — Receptionist is excluded on purpose.
/// Do not expand the policy without a dedicated product decision (Quick Actions already
/// authorizes Receptionist explicitly instead).
/// </summary>
public class StaffRolePolicyTests
{
    private readonly IAuthorizationService _authorization;

    public StaffRolePolicyTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("OwnerOnly", policy => policy.RequireRole("Owner"));
            options.AddPolicy("ManagerOrAbove", policy => policy.RequireRole("Owner", "Manager"));
            options.AddPolicy("AnyStaff", policy => policy.RequireRole("Owner", "Manager", "Trainer"));
        });
        _authorization = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal Principal(string role)
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "Test");
        return new ClaimsPrincipal(identity);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Manager")]
    [InlineData("Trainer")]
    public async Task AnyStaff_AllowsOwnerManagerTrainer(string role)
    {
        var result = await _authorization.AuthorizeAsync(Principal(role), "AnyStaff");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AnyStaff_DoesNotIncludeReceptionist()
    {
        var result = await _authorization.AuthorizeAsync(Principal("Receptionist"), "AnyStaff");
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AnyStaff_DoesNotIncludeMember()
    {
        var result = await _authorization.AuthorizeAsync(Principal("Member"), "AnyStaff");
        Assert.False(result.Succeeded);
    }
}
