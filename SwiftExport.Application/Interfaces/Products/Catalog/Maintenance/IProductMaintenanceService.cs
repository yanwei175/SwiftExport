using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Products.Catalog.Maintenance;

/// <summary>维护产品身份、属性和批量变更，沿用现有版本与事务规则。</summary>
public interface IProductMaintenanceService
{
    /// <summary>新增产品。</summary>
    Task<ServiceResult<SaveItemResult>> AddAsync(CreateItemRequest<ProductDto> item);
    /// <summary>按版本更新产品。</summary>
    Task<ServiceResult<SaveItemResult>> UpdateAsync(UpdateItemRequest<ProductDto> item);
    /// <summary>按版本删除产品。</summary>
    Task<ServiceResult<SaveItemResult>> DeleteAsync(DeleteItemRequest item);
    /// <summary>原子保存产品变更集。</summary>
    Task<ServiceResult<ProductBatchSaveResponse>> BatchSaveAsync(ProductBatchSaveRequest request);
}
