using SwiftExport.Application.Interfaces.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Inventory.Outbounds;
namespace SwiftExport.Application.Services.Inventory.Outbounds;

/// <summary>读取出库列表、详情和选品，不持有写事务。</summary>
public sealed class OutboundQueryService(IOutboundQueryRepository repository, OutboundServiceSupport support) : IOutboundQueryService
{
    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<OutboundManagerListItemDto>>> SearchAsync(OutboundManagerSearchRequest request, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            OutboundServiceSupport.ValidatePage(request);
            if (request.DocumentType.HasValue && !Enum.IsDefined(request.DocumentType.Value))
                throw new OutboundDraftException("出库类型无效。");
            if (request.PostingStatus.HasValue && !Enum.IsDefined(request.PostingStatus.Value))
                throw new OutboundDraftException("过账状态无效。", ServiceErrorCodes.Validation, nameof(request.PostingStatus));
            return await repository.SearchAsync(request, token);
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<OutboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            if (uid == Guid.Empty) throw new OutboundDraftException("出库单标识不能为空。");
            return await repository.GetDetailAsync(uid, token) ?? throw new OutboundDraftException("出库单不存在或已删除。");
        }, token);

    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>> SearchItemsAsync(OutboundItemSearchRequest request, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            OutboundServiceSupport.ValidatePage(request);
            if (request.WarehouseSiteId == Guid.Empty || request.ExcludedProductIds is null || request.ExcludedProductIds.Count > 500)
                throw new OutboundDraftException("请选择仓库，排除产品数量不得超过 500。", ServiceErrorCodes.Validation);
            return await repository.SearchItemsAsync(request, token);
        }, token);

}
