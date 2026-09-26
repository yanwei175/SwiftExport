using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Application.Interfaces.Products.ProductNeutrals.Query;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Products.ProductNeutrals.Query;

/// <summary>读取跨主型号中性产品管理投影，不参与写入事务。</summary>
public sealed class ProductNeutralQueryService(IProductNeutralManagerRepository repository) : IProductNeutralQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>> GetPagedAsync(
        ProductNeutralManagerSearchRequest request,
        CancellationToken cancellationToken = default) =>
        ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>.Ok(
            await repository.GetPagedAsync(request ?? new ProductNeutralManagerSearchRequest(), cancellationToken));
}
