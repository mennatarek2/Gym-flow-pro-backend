namespace GMS.Application.DTOs.Hr;

public class PayrollPeriodDto
{
    public Guid Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string Status { get; set; } = string.Empty;
    public int EmployeeCount { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal DeductionsTotal { get; set; }
    public decimal NetTotal { get; set; }
    public DateTime? CalculatedAtUtc { get; set; }
    public Guid? ApprovedByAppUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
}

public class PayrollLineDto
{
    public Guid Id { get; set; }
    public Guid PayrollPeriodId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public decimal BasicSalary { get; set; }
    public decimal OvertimeAmount { get; set; }
    public decimal BonusAmount { get; set; }
    public decimal AllowanceAmount { get; set; }
    public decimal DeductionAmount { get; set; }
    public decimal NetSalary { get; set; }
    public string PeriodStatus { get; set; } = string.Empty;
}

public class CreatePayrollPeriodRequest
{
    public int Year { get; set; }
    public int Month { get; set; }
}

public class PayrollAdjustmentDto
{
    public Guid Id { get; set; }
    public Guid PayrollPeriodId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class CreatePayrollAdjustmentRequest
{
    public Guid EmployeeId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
}

public sealed class PayrollPaymentDto
{
    public Guid Id { get; set; }
    public Guid PayrollPeriodId { get; set; }
    public Guid PayrollLineId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaidDate { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class CreatePayrollPaymentRequest
{
    public Guid PayrollLineId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly? PaidDate { get; set; }
    public string PaymentMethod { get; set; } = "bank_transfer";
    public string? Reference { get; set; }
}
