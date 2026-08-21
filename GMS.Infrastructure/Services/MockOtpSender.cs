namespace GMS.Infrastructure.Services;

using Microsoft.Extensions.Logging;
using GMS.Core.Interfaces;

/// <summary>
/// Mock OTP sender that logs the OTP to console.
/// Replace with Twilio, WhatsApp Business API, or other SMS provider for production.
/// </summary>
public class MockOtpSender : IOtpSender
{
    private readonly ILogger<MockOtpSender> _logger;

    public MockOtpSender(ILogger<MockOtpSender> logger)
    {
        _logger = logger;
    }

    public Task SendOtpAsync(string phoneNumber, string otp)
    {
        _logger.LogWarning(
            "📱 [MOCK SMS] OTP {Otp} sent to {PhoneNumber}. Replace MockOtpSender with real SMS provider in production.",
            otp, phoneNumber);

        return Task.CompletedTask;
    }
}
