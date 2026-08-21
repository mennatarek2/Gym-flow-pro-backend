using System.Text.RegularExpressions;
using GMS.Application.Interfaces;
using GMS.Application.Options;
using GMS.Core.Entities;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace GMS.Application.Services;

/// <summary>
/// Email OTP delivery strategy using MailKit SMTP.
/// Sends OTP via email with an HTML template. Returns masked email address for user feedback.
/// Masks email as "ah***@gmail.com" (preserves first 3 chars and domain, masks middle).
/// Uses STARTTLS on port 587 by default.
/// </summary>
public class EmailOtpDeliveryStrategy : IOtpDeliveryStrategy
{
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<EmailOtpDeliveryStrategy> _logger;

    public EmailOtpDeliveryStrategy(
        IOptions<EmailSettings> emailSettings,
        ILogger<EmailOtpDeliveryStrategy> logger)
    {
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task<string> SendOtpAsync(
        GymMember member,
        string otp,
        string gymName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(member.Email))
        {
            throw new InvalidOperationException(
                "No email address on file. Please contact gym staff to add your email. " +
                "/ لا يوجد بريد إلكتروني مسجل. تواصل مع موظفي الصالة لإضافته.");
        }

        var maskedEmail = MaskEmailAddress(member.Email);

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_emailSettings.FromName, _emailSettings.FromAddress));
            message.To.Add(new MailboxAddress(member.FullName, member.Email));
            message.Subject = $"Your GymFlowPro verification code — {gymName}";

            // Build HTML email body
            var ttlMinutes = _emailSettings.OtpTtlMinutes ?? 5;
            var body = BuildHtmlEmailBody(otp, gymName, ttlMinutes);

            var bodyPart = new TextPart("html") { Text = body };
            message.Body = bodyPart;

            // Send via SMTP
            using (var client = new SmtpClient())
            {
                await client.ConnectAsync(_emailSettings.SmtpHost, _emailSettings.SmtpPort, SecureSocketOptions.StartTls, cancellationToken);
                await client.AuthenticateAsync(_emailSettings.SmtpUser, _emailSettings.SmtpPassword, cancellationToken);
                await client.SendAsync(message, cancellationToken);
                await client.DisconnectAsync(true, cancellationToken);
            }

            _logger.LogInformation(
                "OTP email sent successfully to {Email} for gym {GymName}.",
                maskedEmail, gymName);

            return maskedEmail;
        }
        catch (SmtpCommandException ex)
        {
            _logger.LogError(ex, "SMTP error sending OTP to {Email}: {Message}", maskedEmail, ex.Message);
            throw new InvalidOperationException(
                "Failed to send verification email. Please try again. " +
                "/ فشل إرسال البريد الإلكتروني. يرجى المحاولة مرة أخرى.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("OTP email send operation was cancelled for {Email}.", maskedEmail);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending OTP email to {Email}: {Message}", maskedEmail, ex.Message);
            throw new InvalidOperationException(
                "Failed to send verification email. Please try again. " +
                "/ فشل إرسال البريد الإلكتروني. يرجى المحاولة مرة أخرى.");
        }
    }

    /// <summary>
    /// Masks email address by preserving first 3 chars and domain, hiding the middle.
    /// Example: "ahmed.ali@gmail.com" → "ahm*****@gmail.com"
    /// </summary>
    private static string MaskEmailAddress(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
            return "****@****";

        var localPart = email[..atIndex];
        var domain = email[(atIndex + 1)..];

        if (localPart.Length <= 3)
            return $"{localPart}****@{domain}";

        var visibleChars = Math.Min(3, localPart.Length);
        var maskedPart = new string('*', Math.Max(1, localPart.Length - visibleChars));

        return $"{localPart[..visibleChars]}{maskedPart}@{domain}";
    }

    /// <summary>
    /// Builds HTML email body for OTP delivery.
    /// Uses inline CSS only (no style block — Gmail strips it).
    /// Mobile-friendly, max-width 480px.
    /// Displays OTP prominently in monospace font with letter-spacing.
    /// </summary>
    private static string BuildHtmlEmailBody(string otp, string gymName, int ttlMinutes)
    {
        var otpFormatted = string.Join(" ", otp.ToCharArray());

        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
</head>
<body style='margin: 0; padding: 0; background-color: #f3f4f6; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif;'>
    <div style='max-width: 480px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1); overflow: hidden;'>
        <!-- Header -->
        <div style='background-color: #111827; color: #ffffff; padding: 24px; text-align: center;'>
            <h1 style='margin: 0; font-size: 24px; font-weight: 600;'>GymFlowPro</h1>
        </div>

        <!-- Body -->
        <div style='padding: 32px 24px;'>
            <p style='margin: 0 0 16px 0; font-size: 16px; color: #374151;'>
                Verification code for <strong>{HtmlEncode(gymName)}</strong>
            </p>

            <!-- OTP Code Box -->
            <div style='background-color: #f9fafb; border: 2px solid #e5e7eb; border-radius: 6px; padding: 24px; text-align: center; margin: 24px 0;'>
                <div style='font-family: ""Courier New"", monospace; font-size: 36px; font-weight: bold; color: #111827; letter-spacing: 8px; word-spacing: 4px; line-height: 1.4;'>
                    {otpFormatted}
                </div>
            </div>

            <!-- TTL Note -->
            <p style='margin: 16px 0; font-size: 14px; color: #6b7280;'>
                This code expires in {ttlMinutes} minutes. Do not share this code with anyone.
            </p>

            <!-- Footer Note -->
            <p style='margin: 24px 0 0 0; font-size: 12px; color: #9ca3af;'>
                If you didn't request this verification code, please ignore this email or contact support.
            </p>
        </div>

        <!-- Footer -->
        <div style='background-color: #f3f4f6; padding: 16px; text-align: center; font-size: 12px; color: #6b7280; border-top: 1px solid #e5e7eb;'>
            <p style='margin: 0;'>© {DateTime.UtcNow.Year} GymFlowPro. All rights reserved.</p>
        </div>
    </div>
</body>
</html>
";
    }

    private static string HtmlEncode(string text)
    {
        return System.Web.HttpUtility.HtmlEncode(text);
    }
}
