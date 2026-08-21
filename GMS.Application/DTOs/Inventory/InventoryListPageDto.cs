namespace GMS.Application.DTOs.Inventory;

/// <summary>List page with hard MaxTake. Controllers may unwrap Items and set X-Gfp-Truncated.</summary>
public class InventoryListPageDto<T>
{
    public List<T> Items { get; set; } = new();
    public bool Truncated { get; set; }
    public int Take { get; set; }
}
