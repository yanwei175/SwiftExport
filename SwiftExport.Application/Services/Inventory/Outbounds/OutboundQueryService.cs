using SwiftExport.Application.Interfaces.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Inventory.Outbounds;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
namespace SwiftExport.Application.Services.Inventory.Outbounds;

/// <summary>按出库查看权限读取列表、详情、选品及仓库候选，不持有写事务。</summary>
public sealed class OutboundQueryService(IOutboundQueryRepository repository, OutboundServiceSupport support,
    IWarehouseQueryRepository warehouses) : IOutboundQueryService
{
    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>> GetWarehouseOptionsAsync(PagedRequestBase request, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            OutboundServiceSupport.ValidatePage(request);
            return await warehouses.GetPickerPagedAsync(new PagedRequestBase
            {
                Keyword = request.Keyword?.Trim(), PageIndex = request.PageIndex, PageSize = request.PageSize
            }, token);
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<OutboundManagerListItemDto>>> SearchAsync(OutboundManagerSearchRequest request, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            OutboundServiceSupport.ValidatePage(request);
            // 集合非 null 时完整覆盖旧单值，空集合不限；只校验实际生效的条件。
            if (request.DocumentTypes is not null && request.DocumentTypes.Any(value => !Enum.IsDefined(value)))
                throw new OutboundDraftException("出库类型集合包含无效值。", ServiceErrorCodes.Validation, nameof(request.DocumentTypes));
            if (request.DocumentTypes is null && request.DocumentType.HasValue && !Enum.IsDefined(request.DocumentType.Value))
                throw new OutboundDraftException("出库类型无效。", ServiceErrorCodes.Validation, nameof(request.DocumentType));
            if (request.PostingStatuses is not null && request.PostingStatuses.Any(value => !Enum.IsDefined(value)))
                throw new OutboundDraftException("过账状态集合包含无效值。", ServiceErrorCodes.Validation, nameof(request.PostingStatuses));
            if (request.PostingStatuses is null && request.PostingStatus.HasValue && !Enum.IsDefined(request.PostingStatus.Value))
                throw new OutboundDraftException("过账状态无效。", ServiceErrorCodes.Validation, nameof(request.PostingStatus));
            return await repository.SearchAsync(request, token);
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<OutboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new OutboundDraftException("出库单标识不能为空。", ServiceErrorCodes.Validation, "Uid");
            return await repository.GetDetailAsync(uid, token) ?? throw new OutboundDraftException("出库单不存在或已删除。");
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>> SearchItemsAsync(OutboundItemSearchRequest request, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            OutboundServiceSupport.ValidatePage(request);
            if (request.WarehouseSiteId == Guid.Empty)
                throw new OutboundDraftException("请选择仓库。", ServiceErrorCodes.Validation, nameof(request.WarehouseSiteId));
            if (request.ExcludedProductIds is null || request.ExcludedProductIds.Count > OutboundQueryLimits.ExcludedProductIdsMax)
                throw new OutboundDraftException($"排除产品数量不得超过 {OutboundQueryLimits.ExcludedProductIdsMax}。", ServiceErrorCodes.Validation, nameof(request.ExcludedProductIds));
            return await repository.SearchItemsAsync(request, token);
        }, token);

}
