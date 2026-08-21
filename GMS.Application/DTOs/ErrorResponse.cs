namespace GMS.Application.DTOs;

/// <summary>
/// DTO for API error responses.
/// </summary>
public class ErrorResponse
{
    public string Message { get; set; }
    public string? Details { get; set; }
    public int StatusCode { get; set; }

    public ErrorResponse(string message, int statusCode = 400, string? details = null)
    {
        Message = message;
        StatusCode = statusCode;
        Details = details;
    }
}
