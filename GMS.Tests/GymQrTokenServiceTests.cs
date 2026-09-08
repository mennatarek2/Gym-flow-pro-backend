namespace GMS.Tests;

using Microsoft.Extensions.Configuration;
using GMS.Application.Interfaces;
using GMS.Application.Services;

/// <summary>
/// Pure crypto-layer tests for the gym QR token (short-lived, signed, no DB round-trip).
/// See CheckinServiceTests / EmployeeAttendanceServiceTests for the end-to-end check-in flows
/// that consume this service.
/// </summary>
public class GymQrTokenServiceTests
{
    private static IGymQrTokenService BuildService(string secret = "Test-Only-Secret-Key-Must-Be-At-Least-32-Characters-Long!") =>
        new GymQrTokenService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:SecretKey"] = secret })
            .Build());

    [Fact]
    public void GenerateThenValidate_RoundTrips_ReturnsSameGymCode()
    {
        var svc = BuildService();
        var (token, expiresAtUtc) = svc.GenerateToken("GYM-CAIRO-01");

        var result = svc.Validate(token);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("GYM-CAIRO-01", result.GymCode);
        Assert.True(expiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public void GenerateToken_DefaultLifetime_IsWithinRecommendedRange()
    {
        var svc = BuildService();
        var (_, expiresAtUtc) = svc.GenerateToken("GYM-CAIRO-01");

        var lifetimeSeconds = (expiresAtUtc - DateTime.UtcNow).TotalSeconds;
        Assert.InRange(lifetimeSeconds, 30, 60);
    }

    [Fact]
    public void Validate_ExpiredToken_Rejected()
    {
        var svc = BuildService();
        var (token, _) = svc.GenerateToken("GYM-CAIRO-01", TimeSpan.FromSeconds(-10));

        var result = svc.Validate(token);

        Assert.False(result.IsValid);
        Assert.Null(result.GymCode);
    }

    [Fact]
    public void Validate_TamperedSignature_Rejected()
    {
        var svc = BuildService();
        var (token, _) = svc.GenerateToken("GYM-CAIRO-01");
        var parts = token.Split('.', 2);
        var signature = parts[1].ToCharArray();
        // Flip one character in the signature segment — must fail the HMAC comparison.
        signature[0] = signature[0] == 'A' ? 'B' : 'A';
        var tampered = parts[0] + "." + new string(signature);

        var result = svc.Validate(tampered);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_WrongSecret_Rejected()
    {
        var minter = BuildService("Secret-A-Must-Be-At-Least-32-Characters-Long-Too!!");
        var verifier = BuildService("Secret-B-Completely-Different-32-Chars-Minimum!!!");
        var (token, _) = minter.GenerateToken("GYM-CAIRO-01");

        var result = verifier.Validate(token);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("GYM-CAIRO-01")]
    [InlineData("a.b.c")]
    [InlineData("...")]
    public void Validate_MalformedInput_RejectedWithoutThrowing(string input)
    {
        var svc = BuildService();

        var result = svc.Validate(input);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GenerateToken_TwoCallsSameGym_ProduceDifferentTokens()
    {
        // Nonce ensures uniqueness even when minted in the same second for the same gym.
        var svc = BuildService();
        var (token1, _) = svc.GenerateToken("GYM-CAIRO-01");
        var (token2, _) = svc.GenerateToken("GYM-CAIRO-01");

        Assert.NotEqual(token1, token2);
    }
}
