using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;

namespace SwiftExport.Application.Services.Inventory.Inbounds;

/// <summary>按入库查看权限读取管理摘要、整单详情及仓库筛选候选。</summary>
public sealed class InboundQueryService(IInboundQueryRepository repository, InboundServiceSupport support,
    IWarehouseQueryRepository warehouses) : IInboundQueryService
{
    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>> GetWarehouseOptionsAsync(PagedRequestBase request, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            InboundServiceSupport.ValidatePage(request);
            return await warehouses.GetPickerPagedAsync(new PagedRequestBase
            {
                Keyword = request.Keyword?.Trim(), PageIndex = request.PageIndex, PageSize = request.PageSize
            }, token);
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>> SearchAsync(InboundManagerSearchRequest request, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            InboundServiceSupport.ValidatePage(request);
            // 集合非空引用时完整覆盖旧单值，空集合表示不限；只校验本次实际生效的条件。
            if (request.DocumentTypes is not null && request.DocumentTypes.Any(value => !Enum.IsDefined(value)))
                throw new DraftException("入库类型集合包含无效值。", ServiceErrorCodes.Validation, nameof(request.DocumentTypes));
            if (request.DocumentTypes is null && request.DocumentType.HasValue && !Enum.IsDefined(request.DocumentType.Value))
                throw new DraftException("入库类型无效。");
            if (request.PostingStatuses is not null && request.PostingStatuses.Any(value => !Enum.IsDefined(value)))
                throw new DraftException("过账状态集合包含无效值。", ServiceErrorCodes.Validation, nameof(request.PostingStatuses));
            if (request.PostingStatuses is null && request.PostingStatus.HasValue && !Enum.IsDefined(request.PostingStatus.Value))
                throw new DraftException("过账状态无效。", ServiceErrorCodes.Validation, nameof(request.PostingStatus));
            return await repository.SearchAsync(request, token);
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<InboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new DraftException("入库单标识不能为空。");
            return await repository.GetDetailAsync(uid, token) ?? throw new DraftException("入库单不存在或已删除。");
        }, token);

}
