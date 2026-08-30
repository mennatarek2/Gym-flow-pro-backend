namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Hr;
using GMS.Core.Entities;

/// <summary>HR-issued one-time Employee App activation codes.</summary>
public interface IEmployeeAppActivationService
{
    Task<Result<EmployeeAppActivationCodeResponse>> GenerateAsync(
        Guid employeeId,
        Guid? createdByIdentityUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locate a still-active code for the tenant, mark consumed (concurrency-safe), return employee.
    /// Caller must run inside an ambient transaction if pairing with token issuance.
    /// </summary>
    Task<Result<Employee>> ConsumeAsync(
        Guid tenantId,
        string activationCodePlaintext,
        CancellationToken cancellationToken = default);
}
