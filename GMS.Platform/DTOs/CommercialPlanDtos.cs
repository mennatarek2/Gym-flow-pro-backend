namespace GMS.Platform.DTOs;

public class CommercialPlanListItemDto
{
    public string Tier { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActiveForSales { get; set; }
    public bool IsDefault { get; set; }
    public decimal MonthlyPriceEgp { get; set; }
    public decimal AnnualPriceEgp { get; set; }
    public decimal AnnualSavingsPercent { get; set; }
    public int? MembersCap { get; set; }
    public int? StaffCap { get; set; }
    public int? BranchesCap { get; set; }
    public int? WhatsAppCap { get; set; }
    public int FeatureCount { get; set; }
    public int LiveSubscriptionCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class CommercialPlanDetailDto : CommercialPlanListItemDto
{
    public IReadOnlyList<string> EnabledFeatures { get; set; } = Array.Empty<string>();
}

public class UpdatePlanMetadataRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class UpdatePlanPricingRequest
{
    public decimal MonthlyPriceEgp { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class UpdatePlanCapsRequest
{
    public int? ActiveMembers { get; set; }
    public int? StaffSeats { get; set; }
    public int? Branches { get; set; }
    public int? WhatsAppMessages { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class UpdatePlanFeaturesRequest
{
    public IReadOnlyList<string> EnabledFeatures { get; set; } = Array.Empty<string>();
    public string Reason { get; set; } = string.Empty;
}

public class UpdatePlanSalesStatusRequest
{
    public bool IsActiveForSales { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class SetDefaultPlanRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class PlanChangeLogDto
{
    public Guid Id { get; set; }
    public string Tier { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public Guid ActorPlatformUserId { get; set; }
    public string? ActorName { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class CommercialPlanMutationResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public CommercialPlanDetailDto? Plan { get; set; }

    public static CommercialPlanMutationResult Ok(CommercialPlanDetailDto plan) =>
        new() { Success = true, Plan = plan };

    public static CommercialPlanMutationResult Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, ErrorMessage = message };
}
