namespace GMS.Api.Authorization;

using System.Security.Claims;
using GMS.Core.Constants;

/// <summary>
/// Critical Close C4 — who may see CostPrice / UnitCost / valuation-adjacent fields.
/// Receptionist has inventory.view only and must not see cost.
/// </summary>
public static class InventoryCostAccess
{
    public static bool CanSeeCost(ClaimsPrincipal user) =>
        user.HasClaim(Permissions.ClaimType, Permissions.InventoryManage)
        || user.HasClaim(Permissions.ClaimType, Permissions.InventoryPurchase)
        || user.HasClaim(Permissions.ClaimType, Permissions.ReportsFinancialView);
}
