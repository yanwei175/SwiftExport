using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Products.BomManagement;

/// <summary>读取成品 BOM 管理投影及编号摘要，不改变产品绑定或组成。</summary>
public interface IBomManagementQueryRepository
{
    /// <summary>按成品资格、绑定状态及资料筛选读取稳定分页。</summary>
    Task<PagedResponseBase<BomProductListItemDto>> SearchProductsAsync(BomProductSearchRequest request, CancellationToken cancellationToken);
    /// <summary>按数据库编号等值规则读取共享数量，空组成也返回摘要。</summary>
    Task<BomNumberInfoDto> GetNumberInfoAsync(string bomNo, CancellationToken cancellationToken);
}
