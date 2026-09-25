using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Query;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Products.ProductMasters.Query;

/// <summary>读取主型号分页和中性产品聚合详情，不拥有写入事务。</summary>
public sealed class ProductMasterQueryService(IProductMasterRepository masterRepository, IProductNeutralRepository neutralRepository) : IProductMasterQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PagedResponseBase<ProductMasterListItemDto>>> GetPagedAsync(ProductMasterSearchRequest request, CancellationToken cancellationToken = default) =>
        ServiceResult<PagedResponseBase<ProductMasterListItemDto>>.Ok(await masterRepository.GetPagedAsync(request ?? new ProductMasterSearchRequest(), cancellationToken));

    /// <inheritdoc />
    public async Task<ServiceResult<ProductMasterDetailDto>> GetDetailAsync(Guid productMasterId, CancellationToken cancellationToken = default)
    {
        if (productMasterId == Guid.Empty)
            return ServiceResult<ProductMasterDetailDto>.Fail(ServiceErrorCodes.Validation, "主型号标识不能为空。", nameof(productMasterId));
        var detail = await masterRepository.GetDetailHeaderAsync(productMasterId, false, cancellationToken);
        if (detail is null)
            return ServiceResult<ProductMasterDetailDto>.Fail(ServiceErrorCodes.BusinessBlocked, "主型号不存在或已删除。");
        detail.Neutrals = (await neutralRepository.GetByProductMasterIdAsync(productMasterId, false, cancellationToken)).ToList();
        return ServiceResult<ProductMasterDetailDto>.Ok(detail);
    }
}
