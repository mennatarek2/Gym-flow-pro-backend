namespace GMS.Application.Interfaces;

using GMS.Application.Common;
using GMS.Application.DTOs.Inventory;

public interface IProductCatalogService
{
    Task<Result<List<ProductCategoryDto>>> ListCategoriesAsync(Guid tenantId);
    Task<Result<ProductCategoryDto>> CreateCategoryAsync(Guid tenantId, CreateProductCategoryRequest request);
    Task<Result<ProductCategoryDto>> UpdateCategoryAsync(Guid tenantId, Guid id, UpdateProductCategoryRequest request);

    Task<Result<List<ProductDto>>> ListProductsAsync(
        Guid tenantId, string? q, Guid? categoryId, bool includeArchived);

    Task<Result<ProductDto>> GetProductAsync(Guid tenantId, Guid id);
    Task<Result<ProductDto>> GetProductByBarcodeAsync(Guid tenantId, string barcode);
    Task<Result<ProductDto>> CreateProductAsync(Guid tenantId, CreateProductRequest request);
    Task<Result<ProductDto>> UpdateProductAsync(Guid tenantId, Guid id, UpdateProductRequest request);
    Task<Result<ProductDto>> ArchiveProductAsync(Guid tenantId, Guid id);
    Task<Result<ProductDto>> UnarchiveProductAsync(Guid tenantId, Guid id);
}
