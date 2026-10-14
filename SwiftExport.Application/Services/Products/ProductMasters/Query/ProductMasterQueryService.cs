using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Query;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Products.Attributes;

namespace SwiftExport.Application.Services.Products.ProductMasters.Query;

/// <summary>读取主型号分页标签摘要，以及包含中性产品和已选标签的聚合详情，不拥有写入事务。</summary>
public sealed class ProductMasterQueryService(IProductMasterRepository masterRepository, IProductNeutralRepository neutralRepository,
    IProductAttributeRepository attributes) : IProductMasterQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PagedResponseBase<ProductMasterListItemDto>>> GetPagedAsync(ProductMasterSearchRequest request, CancellationToken cancellationToken = default)
    {
        var page = await masterRepository.GetPagedAsync(request ?? new ProductMasterSearchRequest(), cancellationToken);
        // 当前页一次读取选择，不为每一行单独查询，也不把标签摘要回写产品类型或系列名。
        var selected = (await attributes.GetSelectionDetailsAsync(ProductAttributeOwnerType.ProductMaster, page.Items.Select(row => row.Uid), cancellationToken)).ToLookup(row => row.OwnerId);
        foreach (var row in page.Items)
            row.TagSummary = string.Join("；", selected[row.Uid].GroupBy(value => value.AttributeGroupId)
                .OrderBy(group => group.First().GroupSortOrder).ThenBy(group => group.Key)
                .Select(group => group.First().GroupName + "：" + string.Join("、", group.OrderBy(value => value.OptionSortOrder).ThenBy(value => value.OptionId)
                    .Select(value => value.OptionName + (value.IsAvailable ? "" : "（已失效）")))));
        return ServiceResult<PagedResponseBase<ProductMasterListItemDto>>.Ok(page);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ProductMasterDetailDto>> GetDetailAsync(Guid productMasterId, CancellationToken cancellationToken = default)
    {
        if (productMasterId == Guid.Empty)
            return ServiceResult<ProductMasterDetailDto>.Fail(ServiceErrorCodes.Validation, "主型号标识不能为空。", nameof(productMasterId));
        var detail = await masterRepository.GetDetailHeaderAsync(productMasterId, false, cancellationToken);
        if (detail is null)
            return ServiceResult<ProductMasterDetailDto>.Fail(ServiceErrorCodes.BusinessBlocked, "主型号不存在或已删除。");
        detail.Neutrals = (await neutralRepository.GetByProductMasterIdAsync(productMasterId, false, cancellationToken)).ToList();
        detail.AttributeSelections = (await attributes.GetSelectionDetailsAsync(ProductAttributeOwnerType.ProductMaster, [productMasterId], cancellationToken)).ToList();
        detail.SelectedOptionIds = detail.AttributeSelections.Select(row => row.OptionId).ToList();
        return ServiceResult<ProductMasterDetailDto>.Ok(detail);
    }

}
