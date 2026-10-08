using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Products.BomManagement;

/// <summary>提供 BOM 管理专用只读用例，校验筛选范围并返回稳定错误。</summary>
public interface IBomManagementQueryService
{
    /// <summary>读取成品 BOM 分页，空结果仍是成功响应。</summary>
    Task<ServiceResult<PagedResponseBase<BomProductListItemDto>>> SearchProductsAsync(BomProductSearchRequest request, CancellationToken cancellationToken);
    /// <summary>读取一个编号的共享使用摘要，未使用编号返回零数量。</summary>
    Task<ServiceResult<BomNumberInfoDto>> GetNumberInfoAsync(string bomNo, CancellationToken cancellationToken);
}
