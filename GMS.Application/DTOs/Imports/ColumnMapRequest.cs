namespace GMS.Application.DTOs.Imports;

/// <summary>Request body for POST /api/imports/{id}/mapping — overrides the auto-detected mapping.</summary>
public class ColumnMapRequest
{
    /// <summary>sourceHeader → targetField (fullName|phoneNumber|planName|startDate|endDate|sessionsRemaining|dateOfBirth).</summary>
    public Dictionary<string, string> Mapping { get; set; } = new();
}
