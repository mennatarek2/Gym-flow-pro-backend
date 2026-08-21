namespace GMS.Application.DTOs.Imports;

/// <summary>Request body for POST /api/imports/{id}/create-plans.</summary>
public class CreatePlansFromImportRequest
{
    public List<ImportPlanSpec> Plans { get; set; } = new();
}
