namespace GMS.Api.Authorization;

using GMS.Application.DTOs.Inventory;

/// <summary>Critical Close C4 — redact cost fields for viewers who fail <see cref="InventoryCostAccess"/>.</summary>
public static class InventoryCostRedaction
{
    public static void RedactProduct(ProductDto? p)
    {
        if (p == null) return;
        p.CostPrice = null;
    }

    public static void RedactProducts(IEnumerable<ProductDto>? rows)
    {
        if (rows == null) return;
        foreach (var p in rows)
            RedactProduct(p);
    }

    public static void RedactPurchaseOrder(PurchaseOrderDto? po)
    {
        if (po?.Lines == null) return;
        foreach (var line in po.Lines)
            line.UnitCost = null;
    }

    public static void RedactPurchaseOrders(IEnumerable<PurchaseOrderDto>? rows)
    {
        if (rows == null) return;
        foreach (var po in rows)
            RedactPurchaseOrder(po);
    }

    public static void RedactGoodsReceipt(GoodsReceiptDto? grn)
    {
        if (grn == null) return;
        grn.TotalAmount = null;
        if (grn.Lines == null) return;
        foreach (var line in grn.Lines)
            line.UnitCost = null;
    }

    public static void RedactGoodsReceiptListItem(GoodsReceiptListItemDto? row)
    {
        if (row == null) return;
        row.TotalAmount = null;
    }

    public static void RedactGoodsReceiptList(IEnumerable<GoodsReceiptListItemDto>? rows)
    {
        if (rows == null) return;
        foreach (var row in rows)
            RedactGoodsReceiptListItem(row);
    }

    public static void RedactMovements(IEnumerable<StockMovementDto>? rows)
    {
        if (rows == null) return;
        foreach (var m in rows)
            m.UnitCost = null;
    }

    public static void RedactMovementReport(IEnumerable<InventoryMovementReportRowDto>? rows)
    {
        if (rows == null) return;
        foreach (var m in rows)
            m.UnitCost = null;
    }

    public static void RedactSupplier(SupplierDto? s)
    {
        if (s == null) return;
        s.PurchasesTotal = null;
        s.PaidTotal = null;
        s.DueTotal = null;
    }

    public static void RedactSuppliers(IEnumerable<SupplierDto>? rows)
    {
        if (rows == null) return;
        foreach (var s in rows)
            RedactSupplier(s);
    }
}
