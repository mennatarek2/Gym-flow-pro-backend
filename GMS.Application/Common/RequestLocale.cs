namespace GMS.Application.Common;

/// <summary>
/// Resolve Accept-Language for email/OTP template selection only.
/// Does not change DTO *Ar fields or Result.Error slash bilingual contract.
/// </summary>
public static class RequestLocale
{
    public static string FromAcceptLanguage(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage)) return "en";
        var primary = acceptLanguage.Split(',')[0].Trim();
        var tag = primary.Split(';')[0].Trim().ToLowerInvariant();
        if (tag.StartsWith("ar", StringComparison.Ordinal)) return "ar";
        return "en";
    }

    public static (string Subject, string HtmlBody) OtpEmail(string locale, string gymName, string code)
    {
        if (locale == "ar")
        {
            return (
                $"رمز التحقق من HyMotion — {gymName}",
                $"""
                <html lang="ar" dir="rtl"><body style="font-family:Tahoma,Arial,sans-serif">
                <p>رمز التحقق الخاص بك لـ <strong>{gymName}</strong>:</p>
                <p style="font-size:24px;letter-spacing:4px"><strong>{code}</strong></p>
                <p>ينتهي صلاحية هذا الرمز قريباً. إذا لم تطلب هذا الرمز، تجاهل هذه الرسالة.</p>
                </body></html>
                """
            );
        }

        return (
            $"Your HyMotion verification code — {gymName}",
            $"""
            <html lang="en"><body style="font-family:Segoe UI,Arial,sans-serif">
            <p>Your verification code for <strong>{gymName}</strong>:</p>
            <p style="font-size:24px;letter-spacing:4px"><strong>{code}</strong></p>
            <p>This code expires soon. If you did not request it, ignore this email.</p>
            </body></html>
            """
        );
    }
}
