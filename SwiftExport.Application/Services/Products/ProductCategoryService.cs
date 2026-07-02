using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Products;

public partial class ProductCategoryService(IProductCategoryRepository repository, ServiceBaseContext context)
    : ApplicationServiceBase(context), IProductCategoryService
{
    private readonly ServiceCrud<ProductCategory, ProductCategoryDto> _crud = new(repository, context);
}
