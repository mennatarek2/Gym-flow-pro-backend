namespace GMS.Platform.Interfaces;

using GMS.Platform.Entities;

public interface IPlatformTokenService
{
    string GenerateAccessToken(PlatformAdminUser user);
    string GenerateMfaSetupToken(PlatformAdminUser user);
    Guid? ValidateMfaSetupToken(string setupToken);
}
