namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Hr;

public interface IPayrollPaymentService
{
    Task<Result<PayrollPaymentDto>> CreateAsync(
        Guid tenantId,
        Guid payrollPeriodId,
        Guid identityUserId,
        CreatePayrollPaymentRequest request,
        CancellationToken ct = default);

    Task<Result<List<PayrollPaymentDto>>> ListAsync(
        Guid tenantId,
        Guid payrollPeriodId,
        CancellationToken ct = default);
}
