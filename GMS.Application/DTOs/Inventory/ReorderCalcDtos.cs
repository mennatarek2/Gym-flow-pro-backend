namespace GMS.Application.DTOs.Inventory;

/// <summary>G1 reorder calculator inputs/outputs (server-owned).</summary>
public static class InventoryReorderDefaults
{
    public const int LookbackDays = 30;
    public const int LeadTimeDays = 7;
}

public class ReorderCalcRow
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? ImageUrl { get; set; }
    public decimal OnHand { get; set; }
    public decimal Available { get; set; }
    public decimal ReorderMinQty { get; set; }
    public decimal SuggestedQty { get; set; }
    public decimal? CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    /// <summary>Average daily retail units sold over lookback (Cairo-agnostic UTC window).</summary>
    public decimal AvgDailySales { get; set; }
    /// <summary>Available / AvgDailySales when velocity &gt; 0; otherwise null.</summary>
    public decimal? DaysOfCover { get; set; }
    /// <summary>Open PO qty remaining + in-transit transfer qty for product.</summary>
    public decimal IncomingOpenQty { get; set; }

    public InventoryReorderSuggestionDto ToSuggestionDto() => new()
    {
        ProductId = ProductId,
        Sku = Sku,
        Name = Name,
        NameAr = NameAr,
        ImageUrl = ImageUrl,
        OnHand = OnHand,
        Available = Available,
        ReorderMinQty = ReorderMinQty,
        SuggestedQty = SuggestedQty,
        CostPrice = CostPrice,
        SellPrice = SellPrice,
        AvgDailySales = AvgDailySales,
        DaysOfCover = DaysOfCover,
        IncomingOpenQty = IncomingOpenQty
    };
}
