namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class ProductCatalogServiceTests
{
    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private static (GymFlowProDbContext ctx, ProductCatalogService svc, Guid tenantId) CreateSut()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Gym", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);
        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Gym",
            NameAr = "صالة",
            GymCode = $"T-{tenantId:N}"[..12],
            City = "Cairo",
            Address = "x",
            PhoneNumber = "01000000000",
            Email = $"{tenantId:N}@test.local",
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        ctx.SaveChanges();

        var svc = new ProductCatalogService(ctx, new NoOpAudit(), NullLogger<ProductCatalogService>.Instance);
        return (ctx, svc, tenantId);
    }

    [Fact]
    public async Task CreateCategoryAndProduct_Succeeds_WithDefaults()
    {
        var (_, svc, tenantId) = CreateSut();

        var cat = await svc.CreateCategoryAsync(tenantId, new CreateProductCategoryRequest
        {
            Name = "Supplements",
            NameAr = "مكملات"
        });
        Assert.True(cat.IsSuccess, cat.Error);

        var product = await svc.CreateProductAsync(tenantId, new CreateProductRequest
        {
            CategoryId = cat.Data!.Id,
            Sku = "PROT-001",
            Barcode = "6223001234567",
            Name = "Protein 2kg",
            SellPrice = 900m,
            CostPrice = 600m,
            TrackStock = true
        });

        Assert.True(product.IsSuccess, product.Error);
        Assert.Equal("EGP", product.Data!.Currency);
        Assert.Equal("pcs", product.Data.UnitOfMeasure);
        Assert.False(product.Data.AllowFractionalQty);
        Assert.True(product.Data.TrackStock);
        Assert.False(product.Data.IsArchived);
        Assert.Equal(cat.Data.Id, product.Data.CategoryId);
    }

    [Fact]
    public async Task CreateProduct_RejectsDuplicateSkuAndBarcode()
    {
        var (_, svc, tenantId) = CreateSut();

        var first = await svc.CreateProductAsync(tenantId, new CreateProductRequest
        {
            Sku = "SKU-1",
            Barcode = "111",
            Name = "A",
            SellPrice = 10
        });
        Assert.True(first.IsSuccess, first.Error);

        var dupSku = await svc.CreateProductAsync(tenantId, new CreateProductRequest
        {
            Sku = "SKU-1",
            Barcode = "222",
            Name = "B",
            SellPrice = 10
        });
        Assert.False(dupSku.IsSuccess);
        Assert.Contains("SKU", dupSku.Error!, StringComparison.OrdinalIgnoreCase);

        var dupBarcode = await svc.CreateProductAsync(tenantId, new CreateProductRequest
        {
            Sku = "SKU-2",
            Barcode = "111",
            Name = "C",
            SellPrice = 10
        });
        Assert.False(dupBarcode.IsSuccess);
        Assert.Contains("Barcode", dupBarcode.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TrackExpiry_RequiresTrackBatch_AndTrackStock()
    {
        var (_, svc, tenantId) = CreateSut();

        var noBatch = await svc.CreateProductAsync(tenantId, new CreateProductRequest
        {
            Sku = "E1",
            Name = "Expiry only",
            SellPrice = 5,
            TrackStock = true,
            TrackBatch = false,
            TrackExpiry = true
        });
        Assert.False(noBatch.IsSuccess);

        var noStock = await svc.CreateProductAsync(tenantId, new CreateProductRequest
        {
            Sku = "E2",
            Name = "Service batch",
            SellPrice = 5,
            TrackStock = false,
            TrackBatch = true,
            TrackExpiry = false
        });
        Assert.False(noStock.IsSuccess);

        var ok = await svc.CreateProductAsync(tenantId, new CreateProductRequest
        {
            Sku = "E3",
            Name = "Batch expiry",
            SellPrice = 5,
            TrackStock = true,
            TrackBatch = true,
            TrackExpiry = true
        });
        Assert.True(ok.IsSuccess, ok.Error);
    }

    [Fact]
    public async Task Archive_HidesFromDefaultList_AndByBarcode()
    {
        var (_, svc, tenantId) = CreateSut();

        var created = await svc.CreateProductAsync(tenantId, new CreateProductRequest
        {
            Sku = "WAT-1",
            Barcode = "999888",
            Name = "Water",
            SellPrice = 10
        });
        Assert.True(created.IsSuccess, created.Error);

        var archived = await svc.ArchiveProductAsync(tenantId, created.Data!.Id);
        Assert.True(archived.IsSuccess);
        Assert.True(archived.Data!.IsArchived);

        var list = await svc.ListProductsAsync(tenantId, null, null, includeArchived: false);
        Assert.Empty(list.Data!);

        var withArchived = await svc.ListProductsAsync(tenantId, null, null, includeArchived: true);
        Assert.Single(withArchived.Data!);

        var byBarcode = await svc.GetProductByBarcodeAsync(tenantId, "999888");
        Assert.False(byBarcode.IsSuccess);
    }

    [Fact]
    public async Task CrossTenant_ProductNotVisible()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var otherTenant = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = otherTenant,
            Name = "Other",
            NameAr = "أخرى",
            GymCode = $"O-{otherTenant:N}"[..12],
            City = "Cairo",
            Address = "y",
            PhoneNumber = "01000000001",
            Email = $"{otherTenant:N}@test.local",
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var created = await svc.CreateProductAsync(tenantId, new CreateProductRequest
        {
            Sku = "T1",
            Name = "Mine",
            SellPrice = 1
        });
        Assert.True(created.IsSuccess, created.Error);

        var other = await svc.GetProductAsync(otherTenant, created.Data!.Id);
        Assert.False(other.IsSuccess);
    }

    private static async Task<Supplier> AddSupplierAsync(
        GymFlowProDbContext ctx, Guid tenantId, string name, bool isActive = true)
    {
        var supplier = new Supplier
        {
            TenantId = tenantId,
            Name = name,
            IsActive = isActive,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Suppliers.Add(supplier);
        await ctx.SaveChangesAsync();
        return supplier;
    }

    private static UpdateProductRequest ToUpdate(ProductDto p) => new()
    {
        CategoryId = p.CategoryId,
        DefaultSupplierId = p.DefaultSupplierId,
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
        CostPrice = p.CostPrice ?? 0m,
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
        IsActive = p.IsActive
    };

    [Fact]
    public async Task CreateProduct_WithoutDefaultSupplier_Succeeds()
    {
        var (_, svc, tenantId) = CreateSut();
        var product = await svc.CreateProductAsync(tenantId, new CreateProductRequest
        {
            Sku = "NS-1",
            Name = "No Supplier",
            SellPrice = 10
        });
        Assert.True(product.IsSuccess, product.Error);
        Assert.Null(product.Data!.DefaultSupplierId);
        Assert.Null(product.Data.DefaultSupplierName);
    }

    [Fact]
    public async Task CreateProduct_WithDefaultSupplier_ReturnsName()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var supplier = await AddSupplierAsync(ctx, tenantId, "ABC Nutrition");

        var product = await svc.CreateProductAsync(tenantId, new CreateProductRequest
        {
            Sku = "PROT-DEF",
            Name = "Protein 1KG",
            SellPrice = 900,
            DefaultSupplierId = supplier.Id
        });

        Assert.True(product.IsSuccess, product.Error);
        Assert.Equal(supplier.Id, product.Data!.DefaultSupplierId);
        Assert.Equal("ABC Nutrition", product.Data.DefaultSupplierName);

        var listed = await svc.ListProductsAsync(tenantId, null, null, includeArchived: false);
        Assert.Equal("ABC Nutrition", listed.Data!.Single().DefaultSupplierName);
    }

    [Fact]
    public async Task UpdateProduct_CanChangeAndRemoveDefaultSupplier()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var abc = await AddSupplierAsync(ctx, tenantId, "ABC Nutrition");
        var xyz = await AddSupplierAsync(ctx, tenantId, "XYZ Sports");

        var created = await svc.CreateProductAsync(tenantId, new CreateProductRequest
        {
            Sku = "PROT-ED",
            Name = "Protein 1KG",
            SellPrice = 900,
            DefaultSupplierId = abc.Id
        });
        Assert.True(created.IsSuccess, created.Error);

        var changed = ToUpdate(created.Data!);
        changed.DefaultSupplierId = xyz.Id;
        var updated = await svc.UpdateProductAsync(tenantId, created.Data!.Id, changed);
        Assert.True(updated.IsSuccess, updated.Error);
        Assert.Equal(xyz.Id, updated.Data!.DefaultSupplierId);
        Assert.Equal("XYZ Sports", updated.Data.DefaultSupplierName);

        var cleared = ToUpdate(updated.Data);
        cleared.DefaultSupplierId = null;
        var removed = await svc.UpdateProductAsync(tenantId, created.Data.Id, cleared);
        Assert.True(removed.IsSuccess, removed.Error);
        Assert.Null(removed.Data!.DefaultSupplierId);
        Assert.Null(removed.Data.DefaultSupplierName);
    }

    [Fact]
    public async Task CreateProduct_RejectsForeignTenantSupplier()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var otherTenant = Guid.NewGuid();
        ctx.Tenants.Add(new Tenant
        {
            Id = otherTenant,
            Name = "Other",
            NameAr = "أخرى",
            GymCode = $"O-{otherTenant:N}"[..12],
            City = "Cairo",
            Address = "y",
            PhoneNumber = "01000000001",
            Email = $"{otherTenant:N}@test.local",
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var foreign = await AddSupplierAsync(ctx, otherTenant, "Gym B Supplier");

        var product = await svc.CreateProductAsync(tenantId, new CreateProductRequest
        {
            Sku = "XT-1",
            Name = "Cross tenant",
            SellPrice = 1,
            DefaultSupplierId = foreign.Id
        });
        Assert.False(product.IsSuccess);
        Assert.Contains("Supplier", product.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateProduct_RejectsInactiveSupplier_AsNewDefault()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var inactive = await AddSupplierAsync(ctx, tenantId, "Old Co", isActive: false);

        var product = await svc.CreateProductAsync(tenantId, new CreateProductRequest
        {
            Sku = "IN-1",
            Name = "Inactive default",
            SellPrice = 1,
            DefaultSupplierId = inactive.Id
        });
        Assert.False(product.IsSuccess);
        Assert.Contains("inactive", product.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateProduct_KeepsExistingDefault_WhenSupplierBecomesInactive()
    {
        var (ctx, svc, tenantId) = CreateSut();
        var supplier = await AddSupplierAsync(ctx, tenantId, "ABC Nutrition");
        var created = await svc.CreateProductAsync(tenantId, new CreateProductRequest
        {
            Sku = "KEEP-1",
            Name = "Keep inactive default",
            SellPrice = 10,
            DefaultSupplierId = supplier.Id
        });
        Assert.True(created.IsSuccess, created.Error);

        supplier.IsActive = false;
        await ctx.SaveChangesAsync();

        var keep = ToUpdate(created.Data!);
        var updated = await svc.UpdateProductAsync(tenantId, created.Data!.Id, keep);
        Assert.True(updated.IsSuccess, updated.Error);
        Assert.Equal(supplier.Id, updated.Data!.DefaultSupplierId);
        Assert.Equal("ABC Nutrition", updated.Data.DefaultSupplierName);
    }
}
