using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Products.ProductNeutrals.Query;

/// <summary>读取跨主型号的中性产品管理投影。</summary>
public interface IProductNeutralQueryService
{
    /// <summary>分页读取中性产品。</summary>
    Task<ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>> GetPagedAsync(ProductNeutralManagerSearchRequest request, CancellationToken cancellationToken = default);
}
