namespace GMS.Core.Entities;

using GMS.Core.Constants;

/// <summary>
/// An employee document (ID scan, contract copy, certificate, ...). The file itself is stored via
/// the existing IFileStorageService (same infrastructure as employee photos), but — unlike photos —
/// FileUrl is never returned to the frontend directly: documents are private HR data, served only
/// through EmployeeDocumentService's protected download (tenant + permission + ownership checked)
/// using IFileStorageService.TryReadAsync. Expiry status (Valid/ExpiringSoon/Expired) is derived at
/// read time against Cairo today, never stored.
/// </summary>
public class EmployeeDocument : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>NationalId | Contract | Certificate | TrainingCertificate | Other — see <see cref="EmployeeDocumentTypes"/>.</summary>
    public string DocumentType { get; set; } = EmployeeDocumentTypes.Other;

    public string FileUrl { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;

    public DateOnly? IssueDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? Notes { get; set; }
    public Guid? UploadedByAppUserId { get; set; }

    public Employee? Employee { get; set; }
}
