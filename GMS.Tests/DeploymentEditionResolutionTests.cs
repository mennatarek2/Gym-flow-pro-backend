namespace GMS.Tests;

using GMS.Core.Configuration;

/// <summary>
/// DeploymentOptions.ResolveEdition must fail safe to SaaS on anything but an explicit,
/// recognized "Local" — a missing or malformed Deployment:Edition config value must never
/// accidentally turn a SaaS deployment into Local.
/// </summary>
public class DeploymentEditionResolutionTests
{
    [Theory]
    [InlineData("SaaS")]
    [InlineData("saas")]
    [InlineData("SAAS")]
    public void ExplicitSaaS_ResolvesToSaaS(string edition)
    {
        Assert.Equal(DeploymentEdition.SaaS, new DeploymentOptions { Edition = edition }.ResolveEdition());
    }

    [Theory]
    [InlineData("Local")]
    [InlineData("local")]
    [InlineData("LOCAL")]
    public void ExplicitLocal_ResolvesToLocal(string edition)
    {
        Assert.Equal(DeploymentEdition.Local, new DeploymentOptions { Edition = edition }.ResolveEdition());
    }

    [Fact]
    public void MissingConfig_DefaultsToSaaS()
    {
        Assert.Equal(DeploymentEdition.SaaS, new DeploymentOptions { Edition = null }.ResolveEdition());
    }

    [Fact]
    public void EmptyStringConfig_DefaultsToSaaS()
    {
        Assert.Equal(DeploymentEdition.SaaS, new DeploymentOptions { Edition = "" }.ResolveEdition());
    }

    [Theory]
    [InlineData("Cloud")]
    [InlineData("On-Prem")]
    [InlineData("locall")]
    [InlineData("Loca")]
    public void UnrecognizedOrMalformedValue_DefaultsToSaaS(string edition)
    {
        Assert.Equal(DeploymentEdition.SaaS, new DeploymentOptions { Edition = edition }.ResolveEdition());
    }

    [Fact]
    public void SurroundingWhitespace_IsTrimmed_StillResolvesToLocal()
    {
        // Enum.TryParse trims leading/trailing whitespace — documented here so this isn't
        // mistaken for a bug if noticed later.
        Assert.Equal(DeploymentEdition.Local, new DeploymentOptions { Edition = "  Local  " }.ResolveEdition());
    }

    [Fact]
    public void LocalEdition_DoesNotForceHttps_SoHealthStaysOnHttp7140()
    {
        Assert.False(GMS.Api.Extensions.ProductionHostingExtensions.ShouldForceHttps(DeploymentEdition.Local));
    }

    [Fact]
    public void SaaSEdition_StillForcesHttps()
    {
        Assert.True(GMS.Api.Extensions.ProductionHostingExtensions.ShouldForceHttps(DeploymentEdition.SaaS));
    }
}
