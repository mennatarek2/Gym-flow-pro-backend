namespace GMS.Tests;

using Microsoft.Extensions.Configuration;
using GMS.Infrastructure.Services;

/// <summary>
/// REM-F2 regression: the AES encryption service must fail fast without an EncryptionKey
/// in production-like environments and must never silently use a known hardcoded key there.
/// Development keeps the historical fallback so local dev/tests are unaffected.
/// </summary>
public class AesEncryptionServiceKeyTests
{
    private static IConfiguration Config(string? env, string? key = null)
    {
        var values = new Dictionary<string, string?>();
        if (env != null) values["ASPNETCORE_ENVIRONMENT"] = env;
        if (key != null) values["EncryptionKey"] = key;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("production")] // case-insensitive
    public void MissingKey_InProductionLikeEnvironment_Throws(string? env)
    {
        Assert.Throws<InvalidOperationException>(() => new AesEncryptionService(Config(env)));
    }

    [Fact]
    public void MissingKey_UnknownEnvironment_UsesExplicitDevFallback()
    {
        var svc = new AesEncryptionService(Config(null));
        Assert.Equal("x", svc.Decrypt(svc.Encrypt("x")));
    }

    [Fact]
    public void MissingKey_InDevelopment_UsesExplicitDevFallback()
    {
        var svc = new AesEncryptionService(Config("Development"));
        var cipher = svc.Encrypt("29801011234567");
        Assert.NotEqual("29801011234567", cipher);
        Assert.Equal("29801011234567", svc.Decrypt(cipher));
    }

    [Fact]
    public void ExplicitKey_Works_InProduction()
    {
        var svc = new AesEncryptionService(Config("Production", "0123456789abcdef0123456789abcdef"));
        const string nationalId = "29801011234567";
        var cipher = svc.Encrypt(nationalId);
        Assert.NotEqual(nationalId, cipher);
        Assert.Equal(nationalId, svc.Decrypt(cipher));
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip_PreservesNationalId()
    {
        var svc = new AesEncryptionService(Config("Development"));
        const string value = "30005061234589";
        Assert.Equal(value, svc.Decrypt(svc.Encrypt(value)));
        Assert.Equal(string.Empty, svc.Encrypt(string.Empty));
        Assert.Equal(string.Empty, svc.Decrypt(string.Empty));
    }
}
