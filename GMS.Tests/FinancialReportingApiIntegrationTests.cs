namespace GMS.Tests;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

public sealed class FinancialReportingApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string JwtSecret =
        "GymFlowPro-Dev-Secret-Key-For-Local-Development-Only-2024-CHANGE-IN-PROD!";
    private static readonly Guid TenantId =
        Guid.Parse("15630168-A8C5-4DB2-B55E-5B64F43AEC24");

    private readonly WebApplicationFactory<Program> _factory;

    public FinancialReportingApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("JwtSettings:SecretKey", JwtSecret);
            builder.UseSetting("JwtSettings:Issuer", "GymFlowPro.API.Dev");
            builder.UseSetting("JwtSettings:Audience", "GymFlowPro.Clients.Dev");
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;Trusted_Connection=true;Encrypt=false;");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JwtSettings:SecretKey"] = JwtSecret,
                    ["JwtSettings:Issuer"] = "GymFlowPro.API.Dev",
                    ["JwtSettings:Audience"] = "GymFlowPro.Clients.Dev",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;Trusted_Connection=true;Encrypt=false;"
                }));
        });
    }

    [Fact]
    public async Task AugustFinancialEndpointsAndDashboardUseCanonicalAvailabilityAwareValues()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateOwnerToken());
        client.DefaultRequestHeaders.Add("X-Gym-Code", "GYM-TEST-01");

        var profitability = await GetJsonAsync(
            client, "/api/reports/profitability?from=2026-08-01&to=2026-08-31");
        var cashFlow = await GetJsonAsync(
            client, "/api/reports/cash-flow?from=2026-08-01&to=2026-08-31");
        var reconciliation = await GetJsonAsync(
            client, "/api/reports/financial-reconciliation?from=2026-08-01&to=2026-08-31");
        var dashboard = await GetJsonAsync(
            client, "/api/dashboard/overview?period=custom&from=2026-08-01&to=2026-08-31");

        Assert.Equal(86032m, profitability.RootElement.GetProperty("collections").GetDecimal());
        Assert.False(profitability.RootElement.GetProperty("settledCashAvailable").GetBoolean());
        Assert.Equal(73456.96m, profitability.RootElement.GetProperty("revenue").GetDecimal());
        Assert.Equal(60261m, profitability.RootElement.GetProperty("cogs").GetDecimal());
        Assert.Equal(13195.96m, profitability.RootElement.GetProperty("grossProfit").GetDecimal());
        Assert.Equal(3967.36m, profitability.RootElement.GetProperty("accountsReceivable").GetDecimal());
        Assert.Equal(190700m, profitability.RootElement.GetProperty("accountsPayable").GetDecimal());
        Assert.False(profitability.RootElement.GetProperty("netProfitAvailable").GetBoolean());
        Assert.Contains(
            "settlement_data_incomplete",
            profitability.RootElement.GetProperty("dataIssues").EnumerateArray()
                .Select(item => item.GetString()));
        Assert.False(cashFlow.RootElement.GetProperty("cashFlowAvailable").GetBoolean());
        Assert.Equal(
            profitability.RootElement.GetProperty("revenue").GetDecimal(),
            reconciliation.RootElement.GetProperty("revenue").GetDecimal());

        var financial = dashboard.RootElement.GetProperty("financial");
        Assert.Equal(
            profitability.RootElement.GetProperty("collections").GetDecimal(),
            financial.GetProperty("collections").GetDecimal());
        Assert.Equal(
            profitability.RootElement.GetProperty("revenue").GetDecimal(),
            financial.GetProperty("revenue").GetDecimal());
        Assert.False(financial.GetProperty("settledCashAvailable").GetBoolean());
        Assert.False(financial.GetProperty("cashFlowAvailable").GetBoolean());
        Assert.False(financial.GetProperty("netProfitAvailable").GetBoolean());
        Assert.Equal(
            "UNAVAILABLE",
            financial.GetProperty("trustStates").GetProperty("SettledCash").GetString());
        Assert.Contains(
            "settlement_data_incomplete",
            financial.GetProperty("financialDataIssues").EnumerateArray()
                .Select(item => item.GetString()));
        Assert.Equal(
            "NO_PAYROLL_PERIOD",
            financial.GetProperty("payrollCoverageStatus").GetString());
        Assert.Equal(JsonValueKind.Null, financial.GetProperty("netProfit").ValueKind);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }

    private static string CreateOwnerToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "Owner"),
            new Claim("role", "Owner"),
            new Claim("tenant_id", TenantId.ToString()),
            new Claim("tenantId", TenantId.ToString()),
            new Claim("TenantId", TenantId.ToString()),
            new Claim("gym_code", "GYM-TEST-01"),
            new Claim("perm", "reports.financial.view"),
            new Claim("perm", "members.view")
        };
        var token = new JwtSecurityToken(
            issuer: "GymFlowPro.API.Dev",
            audience: "GymFlowPro.Clients.Dev",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
