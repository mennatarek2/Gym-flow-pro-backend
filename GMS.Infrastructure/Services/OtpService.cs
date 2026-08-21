namespace GMS.Infrastructure.Services;

using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;

/// <summary>
/// OTP generation, caching, and validation service.
/// Uses IMemoryCache with 5-minute expiry for OTP storage.
/// Key format: "otp:{tenantId}:{phoneNumber}"
/// </summary>
public class OtpService : IOtpService
{
    private readonly IMemoryCache _cache;
    private readonly IOtpSender _otpSender;
    private readonly ILogger<OtpService> _logger;

    private static readonly TimeSpan OtpExpiry = TimeSpan.FromMinutes(5);

    public OtpService(IMemoryCache cache, IOtpSender otpSender, ILogger<OtpService> logger)
    {
        _cache = cache;
        _otpSender = otpSender;
        _logger = logger;
    }

    public async Task<string> GenerateOtpAsync(string phoneNumber, Guid tenantId)
    {
        // Generate 6-digit OTP
        var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        var cacheKey = GetCacheKey(phoneNumber, tenantId);

        // Store in cache with 5-minute sliding expiry
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(OtpExpiry);

        _cache.Set(cacheKey, otp, cacheOptions);

        _logger.LogInformation(
            "OTP generated for phone {PhoneNumber} in tenant {TenantId}. Expires in {ExpiryMinutes} minutes.",
            MaskPhone(phoneNumber), tenantId, OtpExpiry.TotalMinutes);

        // Send OTP via configured sender (SMS, WhatsApp, etc.)
        await _otpSender.SendOtpAsync(phoneNumber, otp);

        return otp;
    }

    public Task<bool> ValidateOtpAsync(string phoneNumber, Guid tenantId, string otp)
    {
        var cacheKey = GetCacheKey(phoneNumber, tenantId);

        if (_cache.TryGetValue(cacheKey, out string? storedOtp) && storedOtp == otp)
        {
            // Remove OTP after successful validation (single-use)
            _cache.Remove(cacheKey);

            _logger.LogInformation(
                "OTP validated successfully for phone {PhoneNumber} in tenant {TenantId}.",
                MaskPhone(phoneNumber), tenantId);

            return Task.FromResult(true);
        }

        _logger.LogWarning(
            "OTP validation failed for phone {PhoneNumber} in tenant {TenantId}.",
            MaskPhone(phoneNumber), tenantId);

        return Task.FromResult(false);
    }

    private static string GetCacheKey(string phoneNumber, Guid tenantId)
        => $"otp:{tenantId}:{phoneNumber}";

    private static string MaskPhone(string phone)
        => phone.Length > 4 ? $"***{phone[^4..]}" : "****";
}
