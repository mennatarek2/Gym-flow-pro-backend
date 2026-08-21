using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GMS.Application.Options;

namespace GMS.Application.Services;

/// <summary>
/// Service for OTP generation, storage, and validation using IMemoryCache.
/// Key format: "otp:{gymCode}:{phoneNumber}" (scoped per gym + phone).
/// Uses cryptographically secure random generation (RandomNumberGenerator, not Random.Shared).
/// Single-use: OTP is removed from cache on successful validation.
/// Prevents OTP farming: if OTP already exists and valid, re-send the same code instead of generating a new one.
/// </summary>
public class OtpCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<OtpCacheService> _logger;
    private readonly OtpDeliveryOptions _otpOptions;

    public OtpCacheService(
        IMemoryCache cache,
        ILogger<OtpCacheService> logger,
        IOptions<OtpDeliveryOptions> otpOptions)
    {
        _cache = cache;
        _logger = logger;
        _otpOptions = otpOptions.Value;
    }

    /// <summary>
    /// Generates a cryptographically random OTP and stores it in cache.
    /// If an OTP already exists and is still valid, returns the cached one (prevents OTP farming).
    /// Cache key format: "otp:{gymCode}:{phoneNumber}".
    /// TTL: Driven by OtpDeliveryOptions.OtpTtlMinutes (default 5 min).
    /// </summary>
    public string GenerateAndStore(string gymCode, string phoneNumber)
    {
        var cacheKey = GetCacheKey(gymCode, phoneNumber);

        // Check if OTP already exists and is valid
        if (_cache.TryGetValue(cacheKey, out string? existingOtp) && !string.IsNullOrEmpty(existingOtp))
        {
            _logger.LogInformation(
                "OTP already cached for gym {GymCode} / phone {Phone}. Returning existing OTP (prevents OTP farming).",
                gymCode, MaskPhoneNumber(phoneNumber));
            return existingOtp;
        }

        // Generate cryptographically random OTP
        var otp = GenerateRandomOtp(_otpOptions.OtpLength);
        var ttl = TimeSpan.FromMinutes(_otpOptions.OtpTtlMinutes);

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(ttl);

        _cache.Set(cacheKey, otp, cacheOptions);

        _logger.LogInformation(
            "OTP generated and cached for gym {GymCode} / phone {Phone}. Expires in {TtlMinutes} minutes.",
            gymCode, MaskPhoneNumber(phoneNumber), _otpOptions.OtpTtlMinutes);

        return otp;
    }

    /// <summary>
    /// Validates the provided OTP against the cached value.
    /// On successful validation, immediately removes the OTP from cache (single-use).
    /// On failed validation, does NOT remove the OTP (allows retries until expiry).
    /// </summary>
    /// <returns>True if valid and consumed; false if invalid or expired.</returns>
    public bool ValidateAndConsume(string gymCode, string phoneNumber, string otp)
    {
        var cacheKey = GetCacheKey(gymCode, phoneNumber);

        if (_cache.TryGetValue(cacheKey, out string? storedOtp) && storedOtp == otp)
        {
            // Successful validation: remove OTP immediately (single-use)
            _cache.Remove(cacheKey);

            _logger.LogInformation(
                "OTP validated and consumed for gym {GymCode} / phone {Phone}.",
                gymCode, MaskPhoneNumber(phoneNumber));

            return true;
        }

        _logger.LogWarning(
            "OTP validation failed for gym {GymCode} / phone {Phone} (invalid or expired).",
            gymCode, MaskPhoneNumber(phoneNumber));

        return false;
    }

    /// <summary>
    /// Generates a cryptographically random numeric OTP.
    /// Uses RandomNumberGenerator for security (not Random.Shared).
    /// </summary>
    private static string GenerateRandomOtp(int length)
    {
        if (length < 1)
            throw new ArgumentException("OTP length must be at least 1.", nameof(length));

        var minValue = (int)Math.Pow(10, length - 1);
        var maxValue = (int)Math.Pow(10, length) - 1;

        var randomNumber = RandomNumberGenerator.GetInt32(minValue, maxValue + 1);
        return randomNumber.ToString($"D{length}");
    }

    private static string GetCacheKey(string gymCode, string phoneNumber)
        => $"otp:{gymCode}:{phoneNumber}";

    private static string MaskPhoneNumber(string phone)
        => phone.Length > 4 ? $"***{phone[^4..]}" : "****";
}
