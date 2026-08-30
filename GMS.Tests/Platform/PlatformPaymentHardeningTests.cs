namespace GMS.Tests.Platform;

using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Platform.Services;

public class PlatformPaymentHardeningTests
{
    private static IConfiguration EmptyPaymobConfig() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration ConfiguredPaymobConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformPaymob:ApiKey"] = "test-api-key",
                ["PlatformPaymob:HmacSecret"] = "test-hmac-secret"
            })
            .Build();

    private static IConfiguration ConfiguredFawryConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformFawry:SecurityKey"] = "fawry-secret"
            })
            .Build();

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Paymob_Charge_MissingApiKey_FailsClosed_InNonDevelopment(string environment)
    {
        using var http = new HttpClient();
        var svc = new PlatformMerchantPaymobService(
            http,
            EmptyPaymobConfig(),
            FakeEnvironment(environment),
            NullLogger<PlatformMerchantPaymobService>.Instance);

        var result = await svc.ChargeSavedCardAsync(Guid.NewGuid(), 100m, "tok", "+201000000000", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("PAYMOB_NOT_CONFIGURED", result.FailureCode);
    }

    [Fact]
    public async Task Paymob_Charge_MissingApiKey_UsesMock_InDevelopment()
    {
        using var http = new HttpClient();
        var svc = new PlatformMerchantPaymobService(
            http,
            EmptyPaymobConfig(),
            FakeEnvironment(Environments.Development),
            NullLogger<PlatformMerchantPaymobService>.Instance);

        var result = await svc.ChargeSavedCardAsync(Guid.NewGuid(), 100m, "tok", "+201000000000", CancellationToken.None);

        Assert.True(result.Success);
        Assert.StartsWith("PM-MOCK-", result.ExternalReference);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Paymob_Webhook_MissingHmacSecret_Rejected_InNonDevelopment(string environment)
    {
        using var http = new HttpClient();
        var svc = new PlatformMerchantPaymobService(
            http,
            EmptyPaymobConfig(),
            FakeEnvironment(environment),
            NullLogger<PlatformMerchantPaymobService>.Instance);

        Assert.False(svc.VerifyWebhookSignature(Encoding.UTF8.GetBytes("{}"), "abc"));
    }

    [Fact]
    public void Paymob_Webhook_MissingHmacSecret_Allowed_InDevelopment()
    {
        using var http = new HttpClient();
        var svc = new PlatformMerchantPaymobService(
            http,
            EmptyPaymobConfig(),
            FakeEnvironment(Environments.Development),
            NullLogger<PlatformMerchantPaymobService>.Instance);

        Assert.True(svc.VerifyWebhookSignature(Encoding.UTF8.GetBytes("{}"), "anything"));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Fawry_Webhook_MissingSecurityKey_Rejected_InNonDevelopment(string environment)
    {
        using var http = new HttpClient();
        var svc = new PlatformMerchantFawryService(
            http,
            EmptyPaymobConfig(),
            FakeEnvironment(environment),
            NullLogger<PlatformMerchantFawryService>.Instance);

        Assert.False(svc.VerifyWebhookSignature(Encoding.UTF8.GetBytes("{}"), "sig"));
    }

    [Fact]
    public void Fawry_Webhook_MissingSecurityKey_Allowed_InDevelopment()
    {
        using var http = new HttpClient();
        var svc = new PlatformMerchantFawryService(
            http,
            EmptyPaymobConfig(),
            FakeEnvironment(Environments.Development),
            NullLogger<PlatformMerchantFawryService>.Instance);

        Assert.True(svc.VerifyWebhookSignature(Encoding.UTF8.GetBytes("{}"), "sig"));
    }

    [Fact]
    public void Paymob_Webhook_ValidHmac_AcceptsMatchingSignature()
    {
        using var http = new HttpClient();
        var config = ConfiguredPaymobConfig();
        var svc = new PlatformMerchantPaymobService(
            http,
            config,
            FakeEnvironment("Production"),
            NullLogger<PlatformMerchantPaymobService>.Instance);

        var body = Encoding.UTF8.GetBytes("{\"ok\":true}");
        using var hmac = new System.Security.Cryptography.HMACSHA512(
            Encoding.UTF8.GetBytes(config["PlatformPaymob:HmacSecret"]!));
        var hex = Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();

        Assert.True(svc.VerifyWebhookSignature(body, hex));
    }

    [Fact]
    public void Fawry_Webhook_ValidSecurityKey_AcceptsMatchingSignature()
    {
        using var http = new HttpClient();
        var config = ConfiguredFawryConfig();
        var svc = new PlatformMerchantFawryService(
            http,
            config,
            FakeEnvironment("Production"),
            NullLogger<PlatformMerchantFawryService>.Instance);

        var body = Encoding.UTF8.GetBytes("{\"invoice\":\"x\"}");
        var key = config["PlatformFawry:SecurityKey"]!;
        var computed = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(body) + key))).ToLowerInvariant();

        Assert.True(svc.VerifyWebhookSignature(body, computed));
    }

    private static IHostEnvironment FakeEnvironment(string name) => new FakeHostEnvironment(name);

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName) => EnvironmentName = environmentName;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "GymFlowPro.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
