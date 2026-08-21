namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Admin;

/// <summary>
/// Service interface for admin operations (staff management).
/// All staff lookups require the caller's tenant id — never load ApplicationUser by Id alone.
/// </summary>
public interface IAdminService
{
    Task<Result<List<StaffListItemDto>>> GetStaffUsersAsync(Guid tenantId);

    Task<Result<StaffDetailDto>> GetStaffUserByIdAsync(Guid tenantId, Guid id);

    Task<Result<StaffDetailDto>> CreateStaffUserAsync(Guid tenantId, CreateStaffRequest request);

    Task<Result<StaffDetailDto>> UpdateStaffUserAsync(Guid tenantId, Guid id, UpdateStaffRequest request);

    Task<Result> DeleteStaffUserAsync(Guid tenantId, Guid id);

    Task<Result> ResetStaffPasswordAsync(Guid tenantId, Guid id, string newPassword);

    Task<Result<StaffDetailDto>> SetStaffPhotoAsync(Guid tenantId, Guid id, Stream image, string fileName, string contentType);

    Task<Result<List<StaffActivityItemDto>>> GetStaffActivityAsync(Guid tenantId, Guid id, int take = 30);
}
