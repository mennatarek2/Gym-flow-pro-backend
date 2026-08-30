namespace GMS.Tests.Platform;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using GMS.Platform.Constants;

/// <summary>
/// P2.2 — role-tier enforcement for platform-api/tenants/{tenantId}/users. Mirrors
/// PlatformIsolationTests' token-minting approach: real HTTP calls through the real Authorize
/// pipeline, so the actual [Authorize(Policy=...)] attributes on the controller are what's being
/// verified, not a hand-rolled substitute.
/// </summary>
public class PlatformTenantUsersAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret =
        "GymFlowPro-Dev-Secret-Key-For-Local-Development-Only-2024-CHANGE-IN-PROD!";

    private readonly WebApplicationFactory<Program> _factory;

    public PlatformTenantUsersAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("JwtSettings:SecretKey", TestJwtSecret);
            b.UseSetting("JwtSettings:Issuer", "GymFlowPro.API.Dev");
        });
    }

    [Theory]
    [InlineData(PlatformRoles.Support)]
    [InlineData(PlatformRoles.Ops)]
    [InlineData(PlatformRoles.Admin)]
    public async Task List_AnyPlatformRole_IsAllowed(string role)
    {
        var client = ClientAs(role);
        var response = await client.GetAsync($"/platform-api/tenants/{Guid.NewGuid()}/users");

        // A made-up tenant has no staff -> 200 with an empty array, not an authorization failure.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Create_SupportRole_IsForbidden()
    {
        var client = ClientAs(PlatformRoles.Support);
        var response = await client.PostAsJsonAsync(
            $"/platform-api/tenants/{Guid.NewGuid()}/users",
            new { fullName = "X", email = "x@example.com", password = "Passw0rd1", role = "Trainer" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(PlatformRoles.Ops)]
    [InlineData(PlatformRoles.Admin)]
    public async Task Create_OpsOrAdmin_PassesAuthorization(string role)
    {
        var client = ClientAs(role);
        var response = await client.PostAsJsonAsync(
            $"/platform-api/tenants/{Guid.NewGuid()}/users",
            new { fullName = "X", email = "x@example.com", password = "Passw0rd1", role = "Trainer" });

        // Authorization passes for Ops/Admin — the made-up tenant then fails at the business-logic
        // layer (seat/tier checks or similar), which is a 4xx too, but never 403 (that would mean
        // the policy itself rejected the caller, which is specifically what this test rules out).
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Disable_SupportRole_IsForbidden()
    {
        var client = ClientAs(PlatformRoles.Support);
        var response = await client.PostAsJsonAsync(
            $"/platform-api/tenants/{Guid.NewGuid()}/users/{Guid.NewGuid()}/disable",
            new { reason = "Testing authorization only, not business logic." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ChangeRole_SupportRole_IsForbidden()
    {
        var client = ClientAs(PlatformRoles.Support);
        var response = await client.PutAsJsonAsync(
            $"/platform-api/tenants/{Guid.NewGuid()}/users/{Guid.NewGuid()}/role",
            new { role = "Manager", reason = "Testing authorization only, not business logic." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Disable_WithoutReason_Returns400_BeforeReachingTheService()
    {
        var client = ClientAs(PlatformRoles.Admin);
        var response = await client.PostAsJsonAsync(
            $"/platform-api/tenants/{Guid.NewGuid()}/users/{Guid.NewGuid()}/disable",
            new { reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient ClientAs(string role)
    {
        var client = _factory.CreateClient();
        var token = MintJwt(role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string MintJwt(string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, "platform-test@example.com"),
            new(ClaimTypes.Role, role),
            new(PlatformAuthConstants.RoleClaimType, role)
        };

        var token = new JwtSecurityToken(
            issuer: "GymFlowPro.API.Dev",
            audience: PlatformAuthConstants.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
