namespace GMS.Platform.Constants;

public static class AutomationSubjectTypes
{
    public const string Member = "member";
    public const string PlatformInvoice = "platform_invoice";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        Member, PlatformInvoice
    };
}

public static class AutomationSequenceKeys
{
    /// <summary>GymFlow platform invoice overdue sequence (CP5).</summary>
    public const string PlatformInvoiceDunning = "platform_invoice_dunning";
}

public static class AutomationHaltReasons
{
    public const string Paid = "paid";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Manual = "manual";
}

/// <summary>Step indices for <see cref="AutomationSequenceKeys.PlatformInvoiceDunning"/>.</summary>
public static class PlatformInvoiceDunningSteps
{
    public const int DueReminder = 0;       // T+0
    public const int SecondReminder = 1;    // T+2
    public const int MarkPastDue = 2;       // T+5
    public const int Suspend = 3;           // grace after past_due
}
