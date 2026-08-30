namespace GMS.Tests.Platform;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using GMS.Platform.Constants;
using GMS.Platform.Persistence;

/// <summary>
/// CP0 isolation: tenant staff JWTs must hard-fail on every /platform-api/* route.
/// </summary>
public class PlatformIsolationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "GymFlowPro-Dev-Secret-Key-For-Local-Development-Only-2024-CHANGE-IN-PROD!";

    private readonly WebApplicationFactory<Program> _factory;

    public PlatformIsolationTests(WebApplicationFactory<Program> factory)
    {
        // appsettings.Development.json ships with an empty JwtSettings:SecretKey (real secret comes
        // from user-secrets/env). Inject the test secret so the SUT's JWT validation uses the same
        // key this test class signs tokens with.
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("JwtSettings:SecretKey", TestJwtSecret);
            b.UseSetting("JwtSettings:Issuer", "GymFlowPro.API.Dev");
        });
    }

    [Fact]
    public async Task PlatformPing_WithTenantAudienceJwt_Returns401()
    {
        // First CP0 acceptance check: a tenant staff JWT must hard-fail on /platform-api/*.
        var client = _factory.CreateClient();
        var tenantToken = MintJwt(audience: "GymFlowPro.Clients.Dev", role: "Owner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);

        var response = await client.GetAsync("/platform-api/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PlatformPing_WithPlatformAudienceJwt_Returns200()
    {
        var client = _factory.CreateClient();
        var platformToken = MintJwt(audience: PlatformAuthConstants.Audience, role: PlatformRoles.Admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", platformToken);

        var response = await client.GetAsync("/platform-api/ping");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but got {(int)response.StatusCode} {response.StatusCode}. Body: {body}. WWW-Authenticate: {string.Join("; ", response.Headers.WwwAuthenticate)}");
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("platform", doc.RootElement.GetProperty("plane").GetString());
    }

    [Fact]
    public void PlatformDbContext_HasNoGlobalQueryFilters()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase("platform-filter-check-" + Guid.NewGuid())
            .Options;

        using var db = new PlatformDbContext(options);
        var entityTypes = db.Model.GetEntityTypes();
        foreach (var entityType in entityTypes)
        {
            Assert.True(
                entityType.GetQueryFilter() == null,
                $"Platform entity {entityType.ClrType.Name} must not have a global query filter.");
        }
    }

    private static string MintJwt(string audience, string role)
    {
        // WebApplicationFactory defaults to Development → JwtSettings from appsettings.Development.json.
        const string secret =
            "GymFlowPro-Dev-Secret-Key-For-Local-Development-Only-2024-CHANGE-IN-PROD!";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, "test@example.com"),
            new(ClaimTypes.Role, role),
            new(PlatformAuthConstants.RoleClaimType, role),
            new("gym_code", "TEST01"),
            new("tenant_id", Guid.NewGuid().ToString())
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

/// <summary>CP0 MFA: login without MFA must force setup (no access token).</summary>
public class PlatformMfaForcedSetupTests
{
    [Fact]
    public async Task Login_WithoutMfaConfigured_ReturnsMfaSetupRequired_NoAccessToken()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase("platform-mfa-" + Guid.NewGuid())
            .Options;

        await using var db = new PlatformDbContext(options);
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<GMS.Platform.Entities.PlatformAdminUser>();
        var user = new GMS.Platform.Entities.PlatformAdminUser
        {
            Email = "ops@gymflow.local",
            FullName = "Ops",
            Role = PlatformRoles.Ops,
            MfaEnabled = false,
            MfaSecret = null,
            IsActive = true
        };
        user.PasswordHash = hasher.HashPassword(user, "Passw0rd!");
        db.PlatformAdminUsers.Add(user);
        await db.SaveChangesAsync();

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "Test-Only-Secret-Key-Must-Be-At-Least-32-Characters-Long!",
                ["JwtSettings:Issuer"] = "GymFlowPro.Tests",
                ["PlatformAuth:BypassMfaInDevelopment"] = "false"
            })
            .Build();

        var tokens = new GMS.Platform.Services.PlatformTokenService(config);
        var mfaStore = new GMS.Platform.Services.LocalEncryptedPlatformMfaSecretStore(
            Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("PlatformMfaTests"),
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GMS.Platform.Services.LocalEncryptedPlatformMfaSecretStore>.Instance);
        var audit = new FakePlatformAudit();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<GMS.Platform.Services.PlatformAuthService>.Instance;
        var env = new FakeHostEnvironment { EnvironmentName = Microsoft.Extensions.Hosting.Environments.Production };
        var auth = new GMS.Platform.Services.PlatformAuthService(
            db, tokens, mfaStore, audit, hasher, env, config, logger);

        var result = await auth.LoginAsync(
            new GMS.Platform.DTOs.PlatformLoginRequest { Email = user.Email, Password = "Passw0rd!" },
            "127.0.0.1");

        Assert.False(result.Success);
        Assert.True(result.MfaSetupRequired);
        Assert.Equal("MFA_SETUP_REQUIRED", result.ErrorCode);
        Assert.Null(result.AccessToken);
        Assert.False(string.IsNullOrWhiteSpace(result.SetupToken));
        Assert.False(string.IsNullOrWhiteSpace(result.OtpAuthUri));
    }

    private sealed class FakePlatformAudit : GMS.Platform.Interfaces.IPlatformAuditService
    {
        public Task LogAsync(
            Guid actorPlatformUserId,
            string action,
            Guid? tenantId = null,
            object? before = null,
            object? after = null,
            string? ipAddress = null) => Task.CompletedTask;

        public Task<GMS.Platform.DTOs.PlatformPagedResult<GMS.Platform.DTOs.PlatformAuditLogDto>> ListAsync(
            Guid? tenantId, string? action, DateOnly? from, DateOnly? to, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GMS.Platform.DTOs.PlatformPagedResult<GMS.Platform.DTOs.PlatformAuditLogDto> { Page = page, PageSize = pageSize });
    }

    private sealed class FakeHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Microsoft.Extensions.Hosting.Environments.Production;
        public string ApplicationName { get; set; } = "GMS.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
