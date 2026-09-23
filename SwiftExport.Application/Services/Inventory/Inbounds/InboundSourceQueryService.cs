using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Inventory;

namespace SwiftExport.Application.Services.Inventory.Inbounds;

/// <summary>按采购来源条件提供只读候选行，不预占收货数量。</summary>
public sealed class InboundSourceQueryService(IInboundManagerRepository repository, InboundServiceSupport support) : IInboundSourceQueryService
{
    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>> SearchSourcesAsync(InboundSourceSearchRequest request, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            InboundServiceSupport.ValidatePage(request);
            if (request.DocumentType != InboundDocumentTypeEnum.PurchaseReceipt)
                throw new DraftException("该入库类型的选源尚未实现。");
            if (!request.PurchaseCategory.HasValue || !Enum.IsDefined(request.PurchaseCategory.Value))
                throw new DraftException("请选择正常采购或客供件采购。");
            if (request.SupplierId == Guid.Empty) throw new DraftException("供应商标识无效。");
            return await repository.SearchSourcesAsync(request, token);
        }, token);

}
