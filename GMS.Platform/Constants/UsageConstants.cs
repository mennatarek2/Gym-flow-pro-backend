namespace GMS.Platform.Constants;

public static class UsageMetrics
{
    public const string ActiveMembers = "active_members";
    public const string WhatsAppMessages = "whatsapp_messages";
    public const string StaffSeats = "staff_seats";
    public const string Branches = "branches";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        ActiveMembers, WhatsAppMessages, StaffSeats, Branches
    };
}

/// <summary>Phase A module keys plus tier capability keys used by IFeatureAccessService.</summary>
public static class FeatureKeys
{
    public const string Sales = "sales";
    public const string Shifts = "shifts";
    public const string Trials = "trials";
    public const string Refunds = "refunds";
    public const string Debtors = "debtors";
    public const string Imports = "imports";
    public const string Inventory = "inventory";
    /// <summary>On Hand board / Move / Count hub — Pro+ packaging (Growth keeps Products desk only).</summary>
    public const string StockManagement = "stock_management";
    /// <summary>HR / Staff Workforce module (employees, departments, positions, contracts, ...).</summary>
    public const string Hr = "hr";

    public static readonly string[] PhaseAModules =
    {
        Sales, Shifts, Trials, Refunds, Debtors, Imports, Inventory, StockManagement, Hr
    };
}

public static class PlanLimitCodes
{
    public const string PlanLimitExceeded = "PLAN_LIMIT_EXCEEDED";
}
