namespace GMS.Platform.DTOs;

public class PlatformLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    /// <summary>Required once MFA is enabled.</summary>
    public string? MfaCode { get; set; }
}

public class PlatformMfaSetupRequest
{
    public string SetupToken { get; set; } = string.Empty;
    public string MfaCode { get; set; } = string.Empty;
}

public class PlatformLoginResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public string? AccessToken { get; set; }
    public int ExpiresInSeconds { get; set; }

    /// <summary>Forced MFA enrollment — no access token until setup completes.</summary>
    public bool MfaSetupRequired { get; set; }
    public string? SetupToken { get; set; }
    public string? OtpAuthUri { get; set; }
    public string? MfaManualKey { get; set; }

    public PlatformAdminDto? User { get; set; }
}

public class PlatformAdminDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool MfaEnabled { get; set; }
}
