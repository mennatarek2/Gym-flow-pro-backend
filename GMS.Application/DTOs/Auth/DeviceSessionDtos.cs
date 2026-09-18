namespace GMS.Application.DTOs.Auth;

/// <summary>Keep-signed-in payload. Refresh token only — never a password.</summary>
public class SaveDeviceSessionRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>Opaque refresh credential for restoring a Local desk session after restart.</summary>
public class DeviceSessionResponse
{
    public string RefreshToken { get; set; } = string.Empty;
}
