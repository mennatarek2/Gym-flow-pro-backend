namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Core.Entities;
using GMS.Infrastructure.Persistence;

/// <summary>
/// INVS-1 product catalog. On-hand qty is not stored here (ledger INVS-3).
/// </summary>
public class ProductCatalogService : IProductCatalogService
{
    private readonly GymFlowProDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<ProductCatalogService> _logger;

    public ProductCatalogService(
        GymFlowProDbContext db,
        IAuditService audit,
        ILogger<ProductCatalogService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<List<ProductCategoryDto>>> ListCategoriesAsync(Guid tenantId)
    {
        var rows = await _db.ProductCategories
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
        return Result<List<ProductCategoryDto>>.Success(rows.Select(MapCategory).ToList());
    }

    public async Task<Result<ProductCategoryDto>> CreateCategoryAsync(
        Guid tenantId, CreateProductCategoryRequest request)
    {
        var entity = new ProductCategory
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            NameAr = NullIfWhiteSpace(request.NameAr),
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.ProductCategories.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("product_category.create", "ProductCategory", entity.Id, null, entity);
        return Result<ProductCategoryDto>.Success(MapCategory(entity));
    }

    public async Task<Result<ProductCategoryDto>> UpdateCategoryAsync(
        Guid tenantId, Guid id, UpdateProductCategoryRequest request)
    {
        var entity = await _db.ProductCategories
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);
        if (entity == null)
            return Result<ProductCategoryDto>.Failure("Category not found / التصنيف غير موجود");

        entity.Name = request.Name.Trim();
        entity.NameAr = NullIfWhiteSpace(request.NameAr);
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("product_category.update", "ProductCategory", entity.Id, null, entity);
        return Result<ProductCategoryDto>.Success(MapCategory(entity));
    }

    public async Task<Result<List<ProductDto>>> ListProductsAsync(
        Guid tenantId, string? q, Guid? categoryId, bool includeArchived)
    {
        var query = _db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.DefaultSupplier)
            .Where(p => p.TenantId == tenantId);

        if (!includeArchived)
            query = query.Where(p => !p.IsArchived);

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p =>
                p.Name.Contains(term)
                || p.Sku.Contains(term)
                || (p.Barcode != null && p.Barcode.Contains(term))
                || (p.NameAr != null && p.NameAr.Contains(term)));
        }

        var rows = await query
            .OrderBy(p => p.Name)
            .Take(500)
            .ToListAsync();

        return Result<List<ProductDto>>.Success(rows.Select(MapProduct).ToList());
    }

    public async Task<Result<ProductDto>> GetProductAsync(Guid tenantId, Guid id)
    {
        var entity = await _db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.DefaultSupplier)
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (entity == null)
            return Result<ProductDto>.Failure("Product not found / المنتج غير موجود");
        return Result<ProductDto>.Success(MapProduct(entity));
    }

    public async Task<Result<ProductDto>> GetProductByBarcodeAsync(Guid tenantId, string barcode)
    {
        var code = (barcode ?? string.Empty).Trim();
        if (code.Length == 0)
            return Result<ProductDto>.Failure("Barcode required / الباركود مطلوب");

        var entity = await _db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.DefaultSupplier)
            .FirstOrDefaultAsync(p =>
                p.TenantId == tenantId
                && p.Barcode == code
                && !p.IsArchived
                && p.IsActive);

        if (entity == null)
            return Result<ProductDto>.Failure("Product not found / المنتج غير موجود");
        return Result<ProductDto>.Success(MapProduct(entity));
    }

    public async Task<Result<ProductDto>> CreateProductAsync(Guid tenantId, CreateProductRequest request)
    {
        var flagError = ValidateTrackFlags(request.TrackStock, request.TrackBatch, request.TrackExpiry);
        if (flagError != null)
            return Result<ProductDto>.Failure(flagError);

        var sku = request.Sku.Trim();
        var barcode = NullIfWhiteSpace(request.Barcode);

        if (await SkuExistsAsync(tenantId, sku, excludeId: null))
            return Result<ProductDto>.Failure("SKU already exists / كود المنتج مستخدم بالفعل");

        if (barcode != null && await BarcodeExistsAsync(tenantId, barcode, excludeId: null))
            return Result<ProductDto>.Failure("Barcode already exists / الباركود مستخدم بالفعل");

        if (request.CategoryId.HasValue)
        {
            var catOk = await _db.ProductCategories
                .AnyAsync(c => c.Id == request.CategoryId && c.TenantId == tenantId);
            if (!catOk)
                return Result<ProductDto>.Failure("Category not found / التصنيف غير موجود");
        }

        var defaultSupplier = await ResolveDefaultSupplierAsync(tenantId, request.DefaultSupplierId, currentlyAssigned: null);
        if (!defaultSupplier.IsSuccess)
            return Result<ProductDto>.Failure(defaultSupplier.Error!);

        var entity = new Product
        {
            TenantId = tenantId,
            CategoryId = request.CategoryId,
            DefaultSupplierId = defaultSupplier.Data,
            Sku = sku,
            Barcode = barcode,
            Name = request.Name.Trim(),
            NameAr = NullIfWhiteSpace(request.NameAr),
            Description = NullIfWhiteSpace(request.Description),
            DescriptionAr = NullIfWhiteSpace(request.DescriptionAr),
            Brand = NullIfWhiteSpace(request.Brand),
            ImageUrl = NullIfWhiteSpace(request.ImageUrl),
            UnitOfMeasure = string.IsNullOrWhiteSpace(request.UnitOfMeasure)
                ? "pcs"
                : request.UnitOfMeasure.Trim(),
            SellPrice = request.SellPrice,
            CostPrice = request.CostPrice,
            Currency = string.IsNullOrWhiteSpace(request.Currency)
                ? "EGP"
                : request.Currency.Trim().ToUpperInvariant(),
            Taxable = request.Taxable,
            VatRatePercent = request.VatRatePercent,
            TrackStock = request.TrackStock,
            TrackBatch = request.TrackBatch,
            TrackExpiry = request.TrackExpiry,
            AllowFractionalQty = request.AllowFractionalQty,
            IsSellable = request.IsSellable,
            IsPurchasable = request.IsPurchasable,
            VisibleToMembers = request.VisibleToMembers,
            ReorderMinQty = request.ReorderMinQty,
            IsActive = request.IsActive,
            IsArchived = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Products.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("product.create", "Product", entity.Id, null, new { entity.Sku, entity.Name });

        return await GetProductAsync(tenantId, entity.Id);
    }

    public async Task<Result<ProductDto>> UpdateProductAsync(
        Guid tenantId, Guid id, UpdateProductRequest request)
    {
        var entity = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (entity == null)
            return Result<ProductDto>.Failure("Product not found / المنتج غير موجود");

        var flagError = ValidateTrackFlags(request.TrackStock, request.TrackBatch, request.TrackExpiry);
        if (flagError != null)
            return Result<ProductDto>.Failure(flagError);

        var referenced = await HasLifecycleReferencesAsync(entity.Id, tenantId);

        var sku = request.Sku.Trim();
        var uom = string.IsNullOrWhiteSpace(request.UnitOfMeasure)
            ? "pcs"
            : request.UnitOfMeasure.Trim();

        if (referenced)
        {
            // Allow healing legacy SKUs corrupted by non-ASCII auto-SKU (stored as ??????-xxxx).
            var skuCorrupted = entity.Sku.Contains('?', StringComparison.Ordinal)
                || entity.Sku.Any(c => c > 127);
            if (!string.Equals(entity.Sku, sku, StringComparison.Ordinal) && !skuCorrupted)
                return Result<ProductDto>.Failure(
                    "SKU cannot change after stock/sale/purchase references / لا يمكن تغيير كود المنتج بعد استخدامه");
            if (!string.Equals(entity.UnitOfMeasure, uom, StringComparison.Ordinal))
                return Result<ProductDto>.Failure(
                    "UnitOfMeasure cannot change after stock movements / لا يمكن تغيير وحدة القياس بعد حركة المخزون");
        }

        // Enabling batch/expiry after non-batch stock exists is blocked when on-hand != 0 (INVS-3+).
        if ((!entity.TrackBatch && request.TrackBatch) || (!entity.TrackExpiry && request.TrackExpiry))
        {
            var onHand = await GetTotalOnHandAsync(entity.Id, tenantId);
            if (onHand != 0)
                return Result<ProductDto>.Failure(
                    "Cannot enable batch/expiry while on-hand stock exists / لا يمكن تفعيل التشغيلة/الصلاحية والمخزون غير صفر");
        }

        if ((entity.TrackBatch && !request.TrackBatch) || (entity.TrackExpiry && !request.TrackExpiry))
        {
            if (await HasBatchHistoryAsync(entity.Id, tenantId))
                return Result<ProductDto>.Failure(
                    "Cannot disable batch/expiry after batch history exists / لا يمكن إلغاء التشغيلة/الصلاحية بعد وجود تاريخ تشغيلات");
        }

        var barcode = NullIfWhiteSpace(request.Barcode);
        if (!string.Equals(entity.Sku, sku, StringComparison.Ordinal)
            && await SkuExistsAsync(tenantId, sku, excludeId: entity.Id))
            return Result<ProductDto>.Failure("SKU already exists / كود المنتج مستخدم بالفعل");

        if (barcode != null
            && !string.Equals(entity.Barcode, barcode, StringComparison.Ordinal)
            && await BarcodeExistsAsync(tenantId, barcode, excludeId: entity.Id))
            return Result<ProductDto>.Failure("Barcode already exists / الباركود مستخدم بالفعل");

        if (request.CategoryId.HasValue)
        {
            var catOk = await _db.ProductCategories
                .AnyAsync(c => c.Id == request.CategoryId && c.TenantId == tenantId);
            if (!catOk)
                return Result<ProductDto>.Failure("Category not found / التصنيف غير موجود");
        }

        var defaultSupplier = await ResolveDefaultSupplierAsync(
            tenantId, request.DefaultSupplierId, currentlyAssigned: entity.DefaultSupplierId);
        if (!defaultSupplier.IsSuccess)
            return Result<ProductDto>.Failure(defaultSupplier.Error!);

        entity.CategoryId = request.CategoryId;
        entity.DefaultSupplierId = defaultSupplier.Data;
        entity.Sku = sku;
        entity.Barcode = barcode;
        entity.Name = request.Name.Trim();
        entity.NameAr = NullIfWhiteSpace(request.NameAr);
        entity.Description = NullIfWhiteSpace(request.Description);
        entity.DescriptionAr = NullIfWhiteSpace(request.DescriptionAr);
        entity.Brand = NullIfWhiteSpace(request.Brand);
        entity.ImageUrl = NullIfWhiteSpace(request.ImageUrl);
        entity.UnitOfMeasure = uom;
        entity.SellPrice = request.SellPrice;
        entity.CostPrice = request.CostPrice;
        entity.Currency = string.IsNullOrWhiteSpace(request.Currency)
            ? "EGP"
            : request.Currency.Trim().ToUpperInvariant();
        entity.Taxable = request.Taxable;
        entity.VatRatePercent = request.VatRatePercent;
        entity.TrackStock = request.TrackStock;
        entity.TrackBatch = request.TrackBatch;
        entity.TrackExpiry = request.TrackExpiry;
        entity.AllowFractionalQty = request.AllowFractionalQty;
        entity.IsSellable = request.IsSellable;
        entity.IsPurchasable = request.IsPurchasable;
        entity.VisibleToMembers = request.VisibleToMembers;
        entity.ReorderMinQty = request.ReorderMinQty;
        entity.IsActive = request.IsActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("product.update", "Product", entity.Id, null, new { entity.Sku, entity.Name });
        return await GetProductAsync(tenantId, entity.Id);
    }

    public async Task<Result<ProductDto>> ArchiveProductAsync(Guid tenantId, Guid id)
    {
        var entity = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (entity == null)
            return Result<ProductDto>.Failure("Product not found / المنتج غير موجود");

        entity.IsArchived = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("product.archive", "Product", entity.Id, null, new { entity.Sku });
        return await GetProductAsync(tenantId, entity.Id);
    }

    public async Task<Result<ProductDto>> UnarchiveProductAsync(Guid tenantId, Guid id)
    {
        var entity = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenantId);
        if (entity == null)
            return Result<ProductDto>.Failure("Product not found / المنتج غير موجود");

        entity.IsArchived = false;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("product.unarchive", "Product", entity.Id, null, new { entity.Sku });
        return await GetProductAsync(tenantId, entity.Id);
    }

    private async Task<bool> SkuExistsAsync(Guid tenantId, string sku, Guid? excludeId)
    {
        return await _db.Products.AnyAsync(p =>
            p.TenantId == tenantId
            && p.Sku == sku
            && (excludeId == null || p.Id != excludeId.Value));
    }

    private async Task<bool> BarcodeExistsAsync(Guid tenantId, string barcode, Guid? excludeId)
    {
        return await _db.Products.AnyAsync(p =>
            p.TenantId == tenantId
            && p.Barcode == barcode
            && (excludeId == null || p.Id != excludeId.Value));
    }

    /// <summary>
    /// True after sale retail lines or any stock movement. SKU/UoM immutability (F10).
    /// </summary>
    private async Task<bool> HasLifecycleReferencesAsync(Guid productId, Guid tenantId)
    {
        var sold = await _db.SaleLines.AnyAsync(l =>
            l.TenantId == tenantId
            && l.LineType == "retail"
            && l.ReferenceId == productId);
        if (sold) return true;

        return await _db.StockMovements.AnyAsync(m =>
            m.TenantId == tenantId && m.ProductId == productId);
    }

    private async Task<decimal> GetTotalOnHandAsync(Guid productId, Guid tenantId)
    {
        return await _db.StockBalances.AsNoTracking()
            .Where(b => b.TenantId == tenantId && b.ProductId == productId)
            .SumAsync(b => (decimal?)b.QtyOnHand) ?? 0m;
    }

    private async Task<bool> HasBatchHistoryAsync(Guid productId, Guid tenantId)
    {
        return await _db.StockMovements.AnyAsync(m =>
            m.TenantId == tenantId && m.ProductId == productId && m.BatchId != null);
    }

    private static string? ValidateTrackFlags(bool trackStock, bool trackBatch, bool trackExpiry)
    {
        if (trackExpiry && !trackBatch)
            return "TrackExpiry requires TrackBatch / تتبع الصلاحية يتطلب تتبع التشغيلة";
        if (!trackStock && (trackBatch || trackExpiry))
            return "Batch/expiry tracking requires TrackStock / تتبع التشغيلة والصلاحية يتطلب تتبع المخزون";
        return null;
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Tenant-safe optional default supplier. Null/empty is valid. New assignments must be
    /// an active same-tenant supplier. An already-saved default may stay even if it later
    /// becomes inactive (do not auto-clear).
    /// </summary>
    private async Task<Result<Guid?>> ResolveDefaultSupplierAsync(
        Guid tenantId, Guid? requestedId, Guid? currentlyAssigned)
    {
        if (!requestedId.HasValue || requestedId.Value == Guid.Empty)
            return Result<Guid?>.Success(null);

        var supplier = await _db.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == requestedId.Value && s.TenantId == tenantId);
        if (supplier == null)
            return Result<Guid?>.Failure("Supplier not found / المورد غير موجود");

        var keepingExisting = currentlyAssigned.HasValue && currentlyAssigned.Value == supplier.Id;
        if (!supplier.IsActive && !keepingExisting)
            return Result<Guid?>.Failure("Supplier is inactive / المورد غير نشط");

        return Result<Guid?>.Success(supplier.Id);
    }

    private static ProductCategoryDto MapCategory(ProductCategory c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        NameAr = c.NameAr,
        SortOrder = c.SortOrder,
        IsActive = c.IsActive,
        CreatedAtUtc = c.CreatedAtUtc
    };

    private static ProductDto MapProduct(Product p) => new()
    {
        Id = p.Id,
        CategoryId = p.CategoryId,
        CategoryName = p.Category?.Name,
        DefaultSupplierId = p.DefaultSupplierId,
        DefaultSupplierName = p.DefaultSupplier?.Name,
        Sku = p.Sku,
        Barcode = p.Barcode,
        Name = p.Name,
        NameAr = p.NameAr,
        Description = p.Description,
        DescriptionAr = p.DescriptionAr,
        Brand = p.Brand,
        ImageUrl = p.ImageUrl,
        UnitOfMeasure = p.UnitOfMeasure,
        SellPrice = p.SellPrice,
        CostPrice = p.CostPrice,
        Currency = p.Currency,
        Taxable = p.Taxable,
        VatRatePercent = p.VatRatePercent,
        TrackStock = p.TrackStock,
        TrackBatch = p.TrackBatch,
        TrackExpiry = p.TrackExpiry,
        AllowFractionalQty = p.AllowFractionalQty,
        IsSellable = p.IsSellable,
        IsPurchasable = p.IsPurchasable,
        VisibleToMembers = p.VisibleToMembers,
        ReorderMinQty = p.ReorderMinQty,
        IsActive = p.IsActive,
        IsArchived = p.IsArchived,
        CreatedAtUtc = p.CreatedAtUtc,
        UpdatedAtUtc = p.UpdatedAtUtc
    };
}
