namespace GMS.Core.Interfaces;

using GMS.Core.Entities;

/// <summary>
/// Domain repository for GymMember operations.
/// </summary>
public interface IMemberRepository
{
    Task<GymMember?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets member by ID with active membership and plan eagerly loaded.
    /// Optimized for check-in flow.
    /// </summary>
    Task<GymMember?> GetByIdWithMembershipAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets member by ID with full details: memberships + recent attendance.
    /// Used for member detail page.
    /// </summary>
    Task<GymMember?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);

    Task<GymMember?> GetByMemberNumberAsync(string memberNumber, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Searches members by name, phone, or member number.
    /// Returns top 20 results ordered by relevance.
    /// </summary>
    Task<List<GymMember>> SearchAsync(string query, Guid tenantId, bool includeInactive = false, CancellationToken ct = default);

    /// <summary>
    /// Gets member by phone number within a tenant.
    /// </summary>
    Task<GymMember?> GetByPhoneAsync(string phoneNumber, Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Gets paginated members with optional search and status filter.
    /// </summary>
    Task<(List<GymMember> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId, string? search, string? status,
        int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Gets the next member sequence number for a tenant (for MemberNumber generation).
    /// </summary>
    Task<int> GetNextMemberSequenceAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Soft-deactivates a member (IsActive = false).
    /// </summary>
    Task DeactivateAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Re-enables a member account (IsActive = true).
    /// </summary>
    Task ReactivateAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(GymMember member, CancellationToken ct = default);
    Task UpdateAsync(GymMember member, CancellationToken ct = default);
}
