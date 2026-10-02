using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.Procurement
{
    /// <summary>采购管理页的列表与明细查询协议；不承载详情编辑、跟进历史或采购命令。</summary>
    public interface IPurchaseOrderManagerApi
    {
        /// <summary>按管理专用条件分页查询采购头摘要。</summary>
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>> SearchAsync(
            [Body] PurchaseOrderManagerSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>一次读取指定采购单的管理从表摘要。</summary>
        [Get("/{purchaseOrderId}/lines")]
        Task<ServiceResult<List<PurchaseOrderManagerLineDto>>> GetLinesAsync(
            Guid purchaseOrderId,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
