using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Products.ProductMasters.Query;

/// <summary>读取主型号列表与含中性产品的详情。</summary>
public interface IProductMasterQueryService
{
    /// <summary>分页读取主型号。</summary>
    Task<ServiceResult<PagedResponseBase<ProductMasterListItemDto>>> GetPagedAsync(ProductMasterSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>读取主型号详情及有效中性产品。</summary>
    Task<ServiceResult<ProductMasterDetailDto>> GetDetailAsync(Guid productMasterId, CancellationToken cancellationToken = default);

    /// <summary>读取最多一万行完整筛选资料及可重新导入的引用文本。</summary>
    Task<ServiceResult<List<SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport.ProductMasterExportRowDto>>> ExportAsync(
        SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport.ProductMasterExportRequest request, CancellationToken cancellationToken = default);
}
