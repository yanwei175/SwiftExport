using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Products;

public class ProductCategoryService(IProductCategoryRepository repository, ServiceBaseContext context) : BaseService<ProductCategory, ProductCategoryDto>(repository, context), IProductCategoryService
{




}
