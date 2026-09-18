namespace GMS.Tests.Platform;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using GMS.Platform.Constants;

/// <summary>
/// D5 HTTP contract: unknown CustomerId is a 400 validation error, not an unhandled 500.
/// </summary>
public class LocalLicenseIssueApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret =
        "GymFlowPro-Dev-Secret-Key-For-Local-Development-Only-2024-CHANGE-IN-PROD!";

    private readonly WebApplicationFactory<Program> _factory;

    public LocalLicenseIssueApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("JwtSettings:SecretKey", TestJwtSecret);
            b.UseSetting("JwtSettings:Issuer", "GymFlowPro.API.Dev");
        });
    }

    [Fact]
    public async Task Issue_UnknownCustomerId_Returns400_WithoutStackTrace()
    {
        var response = await ClientAsOps().PostAsJsonAsync(
            "/platform-api/local-licenses",
            new { customerId = Guid.NewGuid(), deviceLimit = 1 });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Customer was not found.", json.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("at GMS.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ArgumentException", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_MissingCustomerId_Returns400_Required()
    {
        var response = await ClientAsOps().PostAsJsonAsync(
            "/platform-api/local-licenses",
            new { deviceLimit = 1 });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("CustomerId is required.", json.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("at GMS.", body, StringComparison.Ordinal);
    }

    private HttpClient ClientAsOps()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintJwt(PlatformRoles.Ops));
        return client;
    }

    private static string MintJwt(string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, "license-issue-api@test.local"),
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
