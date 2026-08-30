namespace GMS.Application.DTOs.Hr;

public class HrDashboardDto
{
    public int EmployeeCount { get; set; }
    public int PresentToday { get; set; }
    public int LateToday { get; set; }
    public int AbsentToday { get; set; }
    public int OnLeaveToday { get; set; }
    public int OvertimeMinutesToday { get; set; }

    /// <summary>Null when the caller lacks hr.payroll.view — payroll stays private even on the
    /// dashboard, or when the current month has no payroll period yet.</summary>
    public decimal? PayrollNetThisMonth { get; set; }
    public string? PayrollStatusThisMonth { get; set; }

    public int PendingLeaveRequests { get; set; }
    public int UpcomingContractExpirations { get; set; }
    public int ExpiringDocuments { get; set; }
    public int ExpiredDocuments { get; set; }
}
