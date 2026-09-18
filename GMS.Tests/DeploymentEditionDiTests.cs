namespace GMS.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GMS.Application.Interfaces;
using GMS.Core.Interfaces;

/// <summary>
/// Verifies the Phase 1 edition/DI mechanism end to end through the real service registration
/// pipeline: SaaS keeps resolving the Platform-backed subscription/feature services, Local
/// resolves the always-unrestricted Local replacements, and GET /api/deployment/info reports the
/// resolved edition. Uses the same shared dev LocalDB as the other WebApplicationFactory
/// integration tests (see FinancialReportingApiIntegrationTests) — read-only from this test's
/// perspective, so sharing it is safe.
/// </summary>
public sealed class DeploymentEditionDiTests
{
    private const string JwtSecret =
        "GymFlowPro-Dev-Secret-Key-For-Local-Development-Only-2024-CHANGE-IN-PROD!";
    private const string ConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=GymFlowProDb;Trusted_Connection=true;Encrypt=false;";

    private static WebApplicationFactory<Program> BuildFactory(string edition)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // UseSetting (not just ConfigureAppConfiguration) because Program.cs reads all of
            // these synchronously from builder.Configuration before host build completes —
            // ConfigureAppConfiguration's additions land too late for that code to see them
            // (they're only guaranteed visible to services resolved later via DI). Confirmed the
            // hard way: without UseSetting here, Program.cs's Local secret bootstrap saw
            // EncryptionKey/etc. as "missing" and generated+persisted real secrets under this
            // machine's %ProgramData%\HyMotion\config during a test run.
            builder.UseSetting("Deployment:Edition", edition);
            builder.UseSetting("JwtSettings:SecretKey", JwtSecret);
            builder.UseSetting("JwtSettings:Issuer", "GymFlowPro.API.Dev");
            builder.UseSetting("JwtSettings:Audience", "GymFlowPro.Clients.Dev");
            builder.UseSetting("EncryptionKey", "0123456789abcdef0123456789abcdef");
            builder.UseSetting("MemberAppActivation:CodePepper", "test-pepper-member");
            builder.UseSetting("EmployeeAppActivation:CodePepper", "test-pepper-employee");
            builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Deployment:Edition"] = edition,
                    ["JwtSettings:SecretKey"] = JwtSecret,
                    ["JwtSettings:Issuer"] = "GymFlowPro.API.Dev",
                    ["JwtSettings:Audience"] = "GymFlowPro.Clients.Dev",
                    ["EncryptionKey"] = "0123456789abcdef0123456789abcdef",
                    ["MemberAppActivation:CodePepper"] = "test-pepper-member",
                    ["EmployeeAppActivation:CodePepper"] = "test-pepper-employee",
                    ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                }));
        });
    }

    [Fact]
    public async Task SaaSEdition_ResolvesPlatformBackedServices()
    {
        using var factory = BuildFactory("SaaS");
        using var scope = factory.Services.CreateScope();

        Assert.Equal("GMS.Platform.Services.FeatureAccessService",
            scope.ServiceProvider.GetRequiredService<IFeatureAccessService>().GetType().FullName);
        Assert.Equal("GMS.Platform.Services.TierEnforcementService",
            scope.ServiceProvider.GetRequiredService<ITierEnforcementService>().GetType().FullName);
        Assert.Equal("GMS.Platform.Services.SubscriptionAccessService",
            scope.ServiceProvider.GetRequiredService<ISubscriptionAccessService>().GetType().FullName);

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/deployment/info");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SaaS", doc.RootElement.GetProperty("edition").GetString());
    }

    [Fact]
    public async Task LocalEdition_ResolvesLocalReplacementServices()
    {
        using var factory = BuildFactory("Local");
        using var scope = factory.Services.CreateScope();

        Assert.Equal("GMS.Infrastructure.Services.LocalFeatureAccessService",
            scope.ServiceProvider.GetRequiredService<IFeatureAccessService>().GetType().FullName);
        Assert.Equal("GMS.Infrastructure.Services.LocalTierEnforcementService",
            scope.ServiceProvider.GetRequiredService<ITierEnforcementService>().GetType().FullName);
        Assert.Equal("GMS.Infrastructure.Services.LocalSubscriptionAccessService",
            scope.ServiceProvider.GetRequiredService<ISubscriptionAccessService>().GetType().FullName);

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/deployment/info");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Local", doc.RootElement.GetProperty("edition").GetString());
    }

    [Theory]
    [InlineData("SaaS")]
    [InlineData("Local")]
    public void TenantProvisioningService_UnaffectedByEdition_SaaSPathUntouched(string edition)
    {
        using var factory = BuildFactory(edition);
        using var scope = factory.Services.CreateScope();

        // Phase 1/2 never replace ITenantProvisioningService — Local reuses it unmodified via
        // ILocalFirstRunService instead of swapping its registration.
        Assert.Equal("GMS.Application.Services.TenantProvisioningService",
            scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>().GetType().FullName);
    }

    [Fact]
    public void LocalSetupService_OnlyRegisteredInterfaceExistsForBothEditions()
    {
        // ILocalFirstRunService is registered unconditionally (cheap, inert for SaaS); the
        // LocalSetupController itself is what gates by edition (404s on SaaS) — covered by the
        // controller's own edition check, not a DI difference.
        using var saas = BuildFactory("SaaS");
        using var local = BuildFactory("Local");

        Assert.NotNull(saas.Services.CreateScope().ServiceProvider.GetRequiredService<ILocalFirstRunService>());
        Assert.NotNull(local.Services.CreateScope().ServiceProvider.GetRequiredService<ILocalFirstRunService>());
    }

    [Fact]
    public async Task LocalSetupEndpoints_404_OnSaaS()
    {
        using var factory = BuildFactory("SaaS");
        var client = factory.CreateClient();

        var status = await client.GetAsync("/api/local-setup/status");
        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);

        var complete = await client.PostAsJsonAsync("/api/local-setup/complete", new { });
        Assert.Equal(HttpStatusCode.NotFound, complete.StatusCode);
    }

    [Fact]
    public async Task LocalSetupStatus_RespondsOnLocal()
    {
        using var factory = BuildFactory("Local");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/local-setup/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("isCompleted", out _));
    }
}
