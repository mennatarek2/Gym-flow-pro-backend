using System.Globalization;
using GMS.Application.Common;
using Xunit;

namespace GMS.Tests;

public class LocalizationAppErrorTests
{
    [Fact]
    public void AppError_slash_and_body_preserve_stable_code()
    {
        AppMessageCatalog.RegisterForTests("MEMBER_NOT_FOUND", "Member not found", "العضو غير موجود");
        var err = AppMessageCatalog.Get("MEMBER_NOT_FOUND");
        Assert.Equal("MEMBER_NOT_FOUND", err.Code);
        Assert.Equal("Member not found", err.Message);
        Assert.Contains("العضو", err.MessageAr);
        Assert.Contains(" / ", err.Slash);
        Assert.NotNull(err.ToAnonymousBody());
    }

    [Fact]
    public void Result_Failure_AppError_keeps_slash_in_Error()
    {
        var err = AppError.Create("X", "Hello", "مرحبا");
        var result = Result.Failure(err);
        Assert.False(result.IsSuccess);
        Assert.Equal("Hello / مرحبا", result.Error);
        Assert.Equal("Hello", result.Message);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ar-EG")]
    public void Culture_does_not_change_decimal_math(string culture)
    {
        var prev = CultureInfo.CurrentCulture;
        var prevUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            decimal a = 12500.50m;
            decimal b = 2500.25m;
            var net = a - b;
            Assert.Equal(10000.25m, net);
            Assert.Equal("10000.25", net.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
            CultureInfo.CurrentUICulture = prevUi;
        }
    }

    [Theory]
    [InlineData("en", "Your HyMotion")]
    [InlineData("ar-EG,ar;q=0.9", "رمز التحقق")]
    public void Otp_email_templates_follow_Accept_Language(string accept, string expectSnippet)
    {
        var locale = RequestLocale.FromAcceptLanguage(accept);
        var (subject, html) = RequestLocale.OtpEmail(locale, "Test Gym", "123456");
        Assert.Contains(expectSnippet, subject + html);
        Assert.Contains("123456", html);
    }
}
