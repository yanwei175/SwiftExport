using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Inventory;

namespace SwiftExport.Application.Services.Inventory.Inbounds;

/// <summary>按库存模块权限读取入库管理摘要和整单详情。</summary>
public sealed class InboundQueryService(IInboundManagerRepository repository, InboundServiceSupport support) : IInboundQueryService
{
    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>> SearchAsync(InboundManagerSearchRequest request, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            InboundServiceSupport.ValidatePage(request);
            if (request.DocumentType.HasValue && !Enum.IsDefined(request.DocumentType.Value))
                throw new DraftException("入库类型无效。");
            if (request.PostingStatus.HasValue && !Enum.IsDefined(request.PostingStatus.Value))
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
