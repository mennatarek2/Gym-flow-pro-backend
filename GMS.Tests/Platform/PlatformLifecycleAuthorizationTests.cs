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
/// Platform lifecycle authorization matrix — Support read-only, Ops operational mutations,
/// Admin full lifecycle + platform-user admin, tenant JWT blocked.
/// </summary>
public class PlatformLifecycleAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret =
        "GymFlowPro-Dev-Secret-Key-For-Local-Development-Only-2024-CHANGE-IN-PROD!";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public PlatformLifecycleAuthorizationTests(WebApplicationFactory<Program> factory)
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
    public async Task SupportOpsAdmin_CanReadTenantList(string role)
    {
        var response = await ClientAs(role).GetAsync("/platform-api/tenants?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(PlatformRoles.Support)]
    [InlineData(PlatformRoles.Ops)]
    [InlineData(PlatformRoles.Admin)]
    public async Task SupportOpsAdmin_CanReadTenantDetail(string role)
    {
        var response = await ClientAs(role).GetAsync($"/platform-api/tenants/{_tenantId}");
        // Unknown tenant → 404, not 403/401.
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(PlatformRoles.Support)]
    [InlineData(PlatformRoles.Ops)]
    [InlineData(PlatformRoles.Admin)]
    public async Task SupportOpsAdmin_CanReadUsage(string role)
    {
        var response = await ClientAs(role).GetAsync("/platform-api/usage/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(PlatformRoles.Support)]
    [InlineData(PlatformRoles.Ops)]
    [InlineData(PlatformRoles.Admin)]
    public async Task SupportOpsAdmin_CanReadAudit(string role)
    {
        var response = await ClientAs(role).GetAsync("/platform-api/audit?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(PlatformRoles.Support)]
    [InlineData(PlatformRoles.Ops)]
    [InlineData(PlatformRoles.Admin)]
    public async Task SupportOpsAdmin_CanReadRiskQueue(string role)
    {
        var response = await ClientAs(role).GetAsync("/platform-api/risk-queue");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Support_CannotExtendTrial()
    {
        var response = await ClientAs(PlatformRoles.Support).PostAsJsonAsync(
            $"/platform-api/tenants/{_tenantId}/extend-trial",
            new { days = 7, reason = "auth test" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Support_CannotStartTrial()
    {
        var response = await ClientAs(PlatformRoles.Support).PostAsJsonAsync(
            $"/platform-api/tenants/{_tenantId}/start-trial",
            new { tier = "growth" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Support_CannotChangePlan()
    {
        var response = await ClientAs(PlatformRoles.Support).PostAsJsonAsync(
            $"/platform-api/tenants/{_tenantId}/subscription/change-tier",
            new { newTier = "pro", effectiveNow = true, reason = "auth test" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Support_CannotCancelSubscription()
    {
        var response = await ClientAs(PlatformRoles.Support).PostAsJsonAsync(
            $"/platform-api/tenants/{_tenantId}/subscription/cancel",
            new { immediate = false, reason = "auth test" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Support_CannotListPlatformUsers()
    {
        var response = await ClientAs(PlatformRoles.Support).GetAsync("/platform-api/platform-users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/extend-trial")]
    [InlineData("/start-trial")]
    [InlineData("/subscription/convert-trial")]
    [InlineData("/subscription/restart-paid")]
    [InlineData("/force-suspend")]
    [InlineData("/force-reactivate")]
    public async Task Ops_CanReachOperationalMutations_NotForbidden(string suffix)
    {
        var client = ClientAs(PlatformRoles.Ops);
        var url = $"/platform-api/tenants/{_tenantId}{suffix}";
        HttpResponseMessage response = suffix switch
        {
            "/extend-trial" => await client.PostAsJsonAsync(url, new { days = 7, reason = "ops auth test" }),
            "/start-trial" => await client.PostAsJsonAsync(url, new { tier = "growth" }),
            "/subscription/convert-trial" => await client.PostAsJsonAsync(url, new { reason = "ops auth test" }),
            "/subscription/restart-paid" => await client.PostAsJsonAsync(url, new { tier = "growth", reason = "ops auth test" }),
            "/force-suspend" => await client.PostAsJsonAsync(url, new { reason = "ops auth test" }),
            "/force-reactivate" => await client.PostAsJsonAsync(url, new { reason = "ops auth test" }),
            _ => throw new InvalidOperationException()
        };

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("change-tier")]
    [InlineData("cancel")]
    [InlineData("undo-cancel")]
    public async Task Ops_CannotPerformAdminSubscriptionMutations(string action)
    {
        var client = ClientAs(PlatformRoles.Ops);
        var url = $"/platform-api/tenants/{_tenantId}/subscription/{action}";
        HttpResponseMessage response = action switch
        {
            "change-tier" => await client.PostAsJsonAsync(url, new { newTier = "pro", effectiveNow = true, reason = "ops auth test" }),
            "cancel" => await client.PostAsJsonAsync(url, new { immediate = false, reason = "ops auth test" }),
            "undo-cancel" => await client.PostAsJsonAsync(url, new { reason = "ops auth test" }),
            _ => throw new InvalidOperationException()
        };

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ops_CannotAdministerPlatformUsers()
    {
        var response = await ClientAs(PlatformRoles.Ops).GetAsync("/platform-api/platform-users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanListPlatformUsers()
    {
        var response = await ClientAs(PlatformRoles.Admin).GetAsync("/platform-api/platform-users");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanReachChangeTier_NotForbidden()
    {
        var response = await ClientAs(PlatformRoles.Admin).PostAsJsonAsync(
            $"/platform-api/tenants/{_tenantId}/subscription/change-tier",
            new { newTier = "pro", effectiveNow = true, reason = "admin auth test" });
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantJwt_CannotAccessPlatformTenants()
    {
        var client = _factory.CreateClient();
        var tenantToken = MintJwt(audience: "GymFlowPro.Clients.Dev", role: "Owner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);

        var response = await client.GetAsync("/platform-api/tenants");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient ClientAs(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintJwt(role));
        return client;
    }

    private static string MintJwt(string role) =>
        MintJwt(PlatformAuthConstants.Audience, role);

    private static string MintJwt(string audience, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, "platform-lifecycle-auth@test.local"),
            new(ClaimTypes.Role, role),
            new(PlatformAuthConstants.RoleClaimType, role)
        };

        var token = new JwtSecurityToken(
            issuer: "GymFlowPro.API.Dev",
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
