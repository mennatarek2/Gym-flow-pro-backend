namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Api.Filters;
using GMS.Application.DTOs.Inventory;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

/// <summary>INVS-1 product catalog (categories + products). Feature-gated: inventory.</summary>
[Route("api/inventory")]
[Authorize]
[FeatureFlag("inventory")]
public class InventoryCatalogController : BaseApiController
{
    private readonly IProductCatalogService _catalog;
    private readonly ITenantContext _tenantContext;
    private readonly IFileStorageService _files;

    public InventoryCatalogController(
        IProductCatalogService catalog,
        ITenantContext tenantContext,
        IFileStorageService files)
    {
        _catalog = catalog;
        _tenantContext = tenantContext;
        _files = files;
    }

    [HttpGet("categories")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<ProductCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCategories()
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _catalog.ListCategoriesAsync(_tenantContext.TenantId);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    [HttpPost("categories")]
    [HasPermission(Permissions.InventoryManage)]
    [ProducesResponseType(typeof(ProductCategoryDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCategory([FromBody] CreateProductCategoryRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _catalog.CreateCategoryAsync(_tenantContext.TenantId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(ListCategories), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("categories/{id:guid}")]
    [HasPermission(Permissions.InventoryManage)]
    [ProducesResponseType(typeof(ProductCategoryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] UpdateProductCategoryRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _catalog.UpdateCategoryAsync(_tenantContext.TenantId, id, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("products")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(List<ProductDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListProducts(
        [FromQuery] string? q,
        [FromQuery] Guid? categoryId,
        [FromQuery] bool includeArchived = false)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _catalog.ListProductsAsync(
            _tenantContext.TenantId, q, categoryId, includeArchived);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        if (!InventoryCostAccess.CanSeeCost(User))
            InventoryCostRedaction.RedactProducts(result.Data);
        return Ok(result.Data);
    }

    [HttpGet("products/by-barcode/{code}")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBarcode(string code)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _catalog.GetProductByBarcodeAsync(_tenantContext.TenantId, code);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        if (!InventoryCostAccess.CanSeeCost(User))
            InventoryCostRedaction.RedactProduct(result.Data);
        return Ok(result.Data);
    }

    [HttpGet("products/{id:guid}")]
    [HasPermission(Permissions.InventoryView)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProduct(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _catalog.GetProductAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });
        if (!InventoryCostAccess.CanSeeCost(User))
            InventoryCostRedaction.RedactProduct(result.Data);
        return Ok(result.Data);
    }

    [HttpPost("products")]
    [HasPermission(Permissions.InventoryManage)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _catalog.CreateProductAsync(_tenantContext.TenantId, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(GetProduct), new { id = result.Data!.Id }, result.Data);
    }

    /// <summary>
    /// Upload a product photo to local/static storage. Returns a relative URL
    /// (e.g. /uploads/products/{tenant}/{file}) stored on Product.ImageUrl.
    /// </summary>
    [HttpPost("products/image")]
    [HasPermission(Permissions.InventoryManage)]
    [RequestSizeLimit(2 * 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadProductImage(IFormFile? file, CancellationToken ct)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No image uploaded / لم يتم رفع صورة" });

        if (file.Length > 2 * 1024 * 1024)
            return BadRequest(new { error = "Image must be ≤ 2MB / الصورة يجب ألا تتجاوز 2 ميجا" });

        var contentType = (file.ContentType ?? string.Empty).Trim().ToLowerInvariant();
        var isAllowed =
            contentType is "image/jpeg" or "image/jpg" or "image/png" or "image/webp" or "image/gif";
        if (!isAllowed)
            return BadRequest(new { error = "Only JPEG/PNG/WebP/GIF images / صور JPEG أو PNG أو WebP أو GIF فقط" });

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
        {
            extension = contentType switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                _ => ".jpg"
            };
        }

        var safeName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        // Single folder segment — LocalFileStorage embeds folder literally in the URL.
        var folder = $"products-{_tenantContext.TenantId:N}";
        await using var stream = file.OpenReadStream();
        var relativeUrl = await _files.UploadAsync(stream, safeName, folder);

        // Prefer absolute URL so desk/FE on another origin can load the image.
        var absolute = $"{Request.Scheme}://{Request.Host}{relativeUrl}";
        if (absolute.Length > 500)
            absolute = relativeUrl; // fall back to short relative path

        return Ok(new { imageUrl = absolute, relativeUrl });
    }

    [HttpPut("products/{id:guid}")]
    [HasPermission(Permissions.InventoryManage)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest request)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _catalog.UpdateProductAsync(_tenantContext.TenantId, id, request);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("products/{id:guid}/archive")]
    [HasPermission(Permissions.InventoryManage)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Archive(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _catalog.ArchiveProductAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("products/{id:guid}/unarchive")]
    [HasPermission(Permissions.InventoryManage)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Unarchive(Guid id)
    {
        if (!_tenantContext.IsInitialized)
            return Unauthorized(new { error = "Tenant context required." });

        var result = await _catalog.UnarchiveProductAsync(_tenantContext.TenantId, id);
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
        return Ok(result.Data);
    }
}
