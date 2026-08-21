using GMS.Application.DTOs.Admin;
using GMS.Application.DTOs.Members;
using GMS.Application.Services;
using Xunit;

namespace GMS.Tests;

public class AccessCardBrandingPhaseATests
{
    private static MemberDetailDto SampleMember() => new()
    {
        Id = Guid.NewGuid(),
        FullName = "Ahmed Mohamed",
        FullNameAr = "أحمد محمد",
        MemberNumber = "GYM-018"
    };

    [Fact]
    public void Build_UsesDefaultLime_WhenNoBranding()
    {
        var html = AccessCardHtmlBuilder.Build(SampleMember(), "Fitness Hub", "فتنس هب");
        Assert.Contains("background:#7ACC00", html);
        Assert.Contains("Fitness Hub", html);
        Assert.Contains("class=\"mark\"", html);
        Assert.DoesNotContain("class=\"logo\"", html);
    }

    [Fact]
    public void Build_UsesTenantColorAndLogo_WhenProvided()
    {
        var html = AccessCardHtmlBuilder.Build(
            SampleMember(),
            "Elite Fitness",
            "إليت",
            logoUrl: "/uploads/logos-abc/logo.png",
            primaryColor: "#1E90FF",
            showGymLogo: true);

        Assert.Contains("Elite Fitness", html);
        Assert.Contains("src=\"/uploads/logos-abc/logo.png\"", html);
        Assert.Contains("class=\"logo\"", html);
        Assert.DoesNotContain("class=\"mark\"", html);
        Assert.Contains("background:#1E90FF", html);
    }

    [Fact]
    public void Build_FallsBackToMark_WhenShowLogoFalse()
    {
        var html = AccessCardHtmlBuilder.Build(
            SampleMember(),
            "Power Gym",
            string.Empty,
            logoUrl: "/uploads/logos-x/a.png",
            primaryColor: "#FF0000",
            showGymLogo: false);

        Assert.Contains("class=\"mark\"", html);
        Assert.Contains("background:#FF0000", html);
        Assert.DoesNotContain("class=\"logo\"", html);
    }

    [Fact]
    public void NormalizeHex_RejectsInvalid()
    {
        Assert.Null(AccessCardHtmlBuilder.NormalizeHex("red"));
        Assert.Null(AccessCardHtmlBuilder.NormalizeHex("#GG0000"));
        Assert.Equal("#7ACC00", AccessCardHtmlBuilder.NormalizeHex("7acc00"));
        Assert.Equal(BrandingDefaults.PrimaryColor, AccessCardHtmlBuilder.NormalizeHex("#7acc00"));
    }
}
