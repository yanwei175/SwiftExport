using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Contracts.DTOs.Inventory;

namespace SwiftExport.Application.Services.Inventory.Inbounds;

/// <summary>按入库查看权限提供采购来源与供应商筛选项，不预占收货数量。</summary>
public sealed class InboundSourceQueryService(IInboundSourceRepository repository, InboundServiceSupport support,
    IWarehouseQueryRepository warehouses) : IInboundSourceQueryService
{
    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>> GetSupplierOptionsAsync(PagedRequestBase request, CancellationToken token = default) =>
        support.RunAsync(async () =>
        {
            InboundServiceSupport.ValidatePage(request);
            return await warehouses.SearchSupplierFilterOptionsAsync(new PagedRequestBase
            {
                Keyword = request.Keyword?.Trim(), PageIndex = request.PageIndex, PageSize = request.PageSize
            }, token);
        }, token);

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
