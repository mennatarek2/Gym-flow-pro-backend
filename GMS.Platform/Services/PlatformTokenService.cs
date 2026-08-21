namespace GMS.Platform.Services;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using GMS.Platform.Constants;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;

public class PlatformTokenService : IPlatformTokenService
{
    private readonly IConfiguration _configuration;
    private readonly SymmetricSecurityKey _signingKey;

    public PlatformTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
        var secret = _configuration[PlatformAuthConstants.SecretConfigKey]
            ?? throw new InvalidOperationException("JwtSettings:SecretKey is not configured.");
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    public string GenerateAccessToken(PlatformAdminUser user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(PlatformAuthConstants.RoleClaimType, user.Role),
            new(ClaimTypes.Role, user.Role),
            new("full_name", user.FullName),
            new("token_use", "platform_access")
        };

        return WriteToken(claims, TimeSpan.FromMinutes(PlatformAuthConstants.AccessTokenExpirationMinutes));
    }

    public string GenerateMfaSetupToken(PlatformAdminUser user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("token_use", PlatformAuthConstants.SetupPurpose)
        };

        // Short-lived setup ticket — not a platform access token (still uses platform audience so
        // it cannot be used on tenant routes).
        return WriteToken(claims, TimeSpan.FromMinutes(10));
    }

    public Guid? ValidateMfaSetupToken(string setupToken)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _configuration[PlatformAuthConstants.IssuerConfigKey],
            ValidateAudience = true,
            ValidAudience = PlatformAuthConstants.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(setupToken, parameters, out _);
            var use = principal.FindFirst("token_use")?.Value;
            if (!string.Equals(use, PlatformAuthConstants.SetupPurpose, StringComparison.Ordinal))
                return null;

            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(sub, out var id) ? id : null;
        }
        catch
        {
            return null;
        }
    }

    private string WriteToken(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.Add(lifetime),
            Issuer = _configuration[PlatformAuthConstants.IssuerConfigKey],
            Audience = PlatformAuthConstants.Audience,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256Signature)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
