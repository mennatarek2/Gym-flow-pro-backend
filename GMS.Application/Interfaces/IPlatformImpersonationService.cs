namespace GMS.Application.Interfaces;

using GMS.Platform.DTOs;

public interface IPlatformImpersonationService
{
    Task<ImpersonationCreateResult> CreateAsync(
        Guid tenantId,
        Guid platformUserId,
        string reason,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}

public class ImpersonationCreateResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public ImpersonationResponse? Value { get; set; }

    public static ImpersonationCreateResult Ok(ImpersonationResponse value) => new()
    {
        Success = true,
        Value = value
    };

    public static ImpersonationCreateResult Fail(string code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message
    };
}
