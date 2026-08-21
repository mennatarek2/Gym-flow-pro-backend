namespace GMS.Tests;

using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using GMS.Core.Constants;
using GMS.Core.Entities.Identity;
using GMS.Infrastructure.Services;

public class TokenServiceJwtRoundTripTests
{
    private static TokenService BuildTokenService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "Test-Only-Secret-Key-Must-Be-At-Least-32-Characters-Long!",
                ["JwtSettings:Issuer"] = "GymFlowPro.Tests",
                ["JwtSettings:Audience"] = "GymFlowPro.Tests.Clients",
                ["JwtSettings:AccessTokenExpirationMinutes"] = "15"
            })
            .Build();

        return new TokenService(configuration);
    }

    [Fact]
    public async Task GeneratedToken_ContainsOnePermClaimPerGrantedPermission()
    {
        var tokenService = BuildTokenService();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "receptionist@gymflow.test",
            FirstName = "Laila",
            LastName = "Reception"
        };
        var tenantId = Guid.NewGuid();
        var permissions = new DefaultPermissionProvider().GetPermissions(new[] { "Receptionist" });

        var token = await tokenService.GenerateAccessTokenAsync(
            user, tenantId, "GYM-TEST-01", new[] { "Receptionist" }, permissions);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var claimedPermissions = jwt.Claims
            .Where(c => c.Type == Permissions.ClaimType)
            .Select(c => c.Value)
            .ToHashSet();

        Assert.Equal(permissions.Count, claimedPermissions.Count);
        foreach (var permission in permissions)
            Assert.Contains(permission, claimedPermissions);

        // Sanity: permissions withheld from receptionist must not leak into the token.
        Assert.DoesNotContain(Permissions.PlansManage, claimedPermissions);
        Assert.DoesNotContain(Permissions.MembershipsFreeze, claimedPermissions);
    }

    [Fact]
    public async Task GeneratedToken_WithNoPermissions_HasNoPermClaims()
    {
        var tokenService = BuildTokenService();
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "member@gymflow.test" };

        var token = await tokenService.GenerateAccessTokenAsync(
            user, Guid.NewGuid(), "GYM-TEST-01", new[] { "Member" });

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.DoesNotContain(jwt.Claims, c => c.Type == Permissions.ClaimType);
    }
}
