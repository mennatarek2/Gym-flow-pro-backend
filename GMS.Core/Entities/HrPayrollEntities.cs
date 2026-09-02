namespace GMS.Core.Entities;

using GMS.Core.Constants;

/// <summary>
/// One payroll run for one calendar month. Draft/Calculated periods can be recalculated freely;
/// Approved/Closed periods are frozen — see PayrollPeriodService for the exact lifecycle rules.
/// </summary>
public class PayrollPeriod : BaseEntity
{
    public Guid TenantId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>Draft | Calculated | Approved | Closed — see <see cref="PayrollPeriodStatuses"/>.</summary>
    public string Status { get; set; } = PayrollPeriodStatuses.Draft;

    public DateTime? CalculatedAtUtc { get; set; }
    public Guid? ApprovedByAppUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    public Tenant? Tenant { get; set; }
    public ICollection<PayrollLine> Lines { get; set; } = new List<PayrollLine>();
}

/// <summary>
/// One employee's payroll line for one period. BasicSalary is snapshotted from the employee's
/// current EmployeeContract at calculation time — a later contract change (e.g. a raise) never
/// rewrites an already-calculated line, preserving historical payroll integrity. Frozen once the
/// parent PayrollPeriod is Approved/Closed.
/// </summary>
public class PayrollLine : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollPeriodId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>The EmployeeContract this line's BasicSalary was snapshotted from. Nullable — an
    /// employee with no current contract still gets a (zero-basic-salary) line rather than being
    /// silently skipped, so payroll coverage is visibly complete for the period.</summary>
    public Guid? ContractId { get; set; }

    public decimal BasicSalary { get; set; }
    public decimal OvertimeAmount { get; set; }
    public decimal BonusAmount { get; set; }
    public decimal AllowanceAmount { get; set; }
    public decimal DeductionAmount { get; set; }
    public decimal NetSalary { get; set; }

    public Tenant? Tenant { get; set; }
    public PayrollPeriod? PayrollPeriod { get; set; }
    public Employee? Employee { get; set; }
    public EmployeeContract? Contract { get; set; }
}

/// <summary>
/// A manual bonus/allowance/overtime/deduction input for one employee in one period. Consumed the
/// next time that period is (re)calculated; only creatable while the period is Draft/Calculated.
/// </summary>
public class PayrollAdjustment : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollPeriodId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>Bonus | Allowance | Overtime | Deduction — see <see cref="PayrollAdjustmentTypes"/>.</summary>
    public string Type { get; set; } = PayrollAdjustmentTypes.Bonus;

    public decimal Amount { get; set; }
    public string? Reason { get; set; }
    public Guid? CreatedByAppUserId { get; set; }

    public Tenant? Tenant { get; set; }
    public PayrollPeriod? PayrollPeriod { get; set; }
    public Employee? Employee { get; set; }
}

/// <summary>
/// Actual payroll disbursement. It is separate from payroll calculation and
/// liability recognition so cash flow cannot be inferred from payroll totals.
/// </summary>
public class PayrollPayment : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid PayrollPeriodId { get; set; }
    public Guid PayrollLineId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaidDate { get; set; }
    public string PaymentMethod { get; set; } = "bank_transfer";
    public string? Reference { get; set; }
    public Guid PaidByAppUserId { get; set; }
    public Guid CashExpenseId { get; set; }
    public Guid? CashMovementId { get; set; }
    public string Status { get; set; } = "posted";

    public Tenant? Tenant { get; set; }
    public PayrollPeriod? PayrollPeriod { get; set; }
    public PayrollLine? PayrollLine { get; set; }
    public AppUser? PaidByAppUser { get; set; }
    public CashExpense? CashExpense { get; set; }
    public CashMovement? CashMovement { get; set; }
}
